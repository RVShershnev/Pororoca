using Moq;
using Pororoca.Domain.Features.Entities.Pororoca;
using Pororoca.Domain.Features.Entities.Pororoca.Http;
using Pororoca.Domain.Features.Entities.Pororoca.WebSockets;
using Pororoca.Domain.Features.TranslateRequest;
using Pororoca.Domain.Features.VariableResolution;
using Xunit;
using static Pororoca.Domain.Features.TranslateRequest.Common.PororocaRequestCommonValidator;
using static Pororoca.Domain.Features.TranslateRequest.WebSockets.Connection.PororocaWebSocketConnectionValidator;

namespace Pororoca.Domain.Tests.Features.TranslateRequest.WebSockets.Connection;

public static class PororocaWebSocketConnectionValidatorTests
{
    #region MOCKERS

    private static HttpVersionAvailableVerifier MockHttpVersionOSVerifier(bool valid, string? errorCode) =>
        (decimal x, out string? z) =>
        {
            z = errorCode;
            return valid;
        };

    private static Mock<IPororocaVariableResolver> MockVariableResolver(string key, string value) =>
        MockVariableResolver(new() { { key, value } });

    private static Mock<IPororocaVariableResolver> MockVariableResolver(Dictionary<string, string> kvs)
    {
        Mock<IPororocaVariableResolver> mockedVariableResolver = new();

        string f(string? k) => k == null ? string.Empty : kvs.TryGetValue(k, out string? value) ? value! : k;

        mockedVariableResolver.Setup(x => x.ReplaceTemplates(It.IsAny<string?>()))
                              .Returns((Func<string?, string>)f);

        return mockedVariableResolver;
    }

    private static FileExistsVerifier MockFileExistsVerifier(bool exists) =>
        (string s) => exists;

    private static FileExistsVerifier MockFileExistsVerifier(Dictionary<string, bool> fileList) =>
        (string s) =>
        {
            bool exists = fileList.ContainsKey(s) && fileList[s];
            return exists;
        };

    #endregion

    #region IS VALID REQUEST URL

    [Fact]
    public static void Should_detect_invalid_connection_if_URL_is_invalid()
    {
        // GIVEN
        const string urlTemplate = "{{url}}";
        const string url = "url/api/qry?x=abc";
        var mockedVariableResolver = MockVariableResolver(urlTemplate, url);
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaWebSocketConnection ws = new();
        ws.Url = urlTemplate;

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates(urlTemplate), Times.Once);
        Assert.Equal(TranslateRequestErrors.InvalidUrl, errorCode);
    }

    [Fact]
    public static void Should_allow_connection_if_URL_is_valid()
    {
        // GIVEN
        const string urlTemplate = "{{url}}";
        const string url = "ws://www.pudim.com.br";
        var mockedVariableResolver = MockVariableResolver(urlTemplate, url);
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaWebSocketConnection ws = new();
        ws.Url = urlTemplate;

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates(urlTemplate), Times.Once);
        Assert.Null(errorCode);
    }

    #endregion

    #region IS VALID WEBSOCKET HTTP VERSION

    [Fact]
    public static void Should_detect_invalid_request_if_websocket_http_version_is_not_supported()
    {
        // GIVEN
        const string urlTemplate = "{{url}}";
        const string url = "ws://www.url.br";
        var mockedVariableResolver = MockVariableResolver(urlTemplate, url);
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(false, TranslateRequestErrors.WebSocketHttpVersionUnavailable);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaWebSocketConnection ws = new();
        ws.Url = urlTemplate;
        ws.HttpVersion = 2.0m;

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates(urlTemplate), Times.Once);
        Assert.Equal(TranslateRequestErrors.WebSocketHttpVersionUnavailable, errorCode);
    }

    [Fact]
    public static void Should_allow_connection_if_websocket_http_version_is_supported()
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new());
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.url.br";
        ws.HttpVersion = 2.0m;

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out _, out string? errorCode));

        // THEN
        Assert.Null(errorCode);
    }

    #endregion

    #region IS VALID CLIENT CERTIFICATE AUTH

    [Fact]
    public static void Should_reject_ws_with_client_certificate_auth_if_PKCS12_cert_file_is_not_found()
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver("{{CertificateFilePath}}", "./cert.p12");
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(false);
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pkcs12, "{{CertificateFilePath}}", null, "prvkeypwd");
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{CertificateFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("prvkeypwd"), Times.Once);
        Assert.Equal(TranslateRequestErrors.ClientCertificateFileNotFound, errorCode);
    }

    [Fact]
    public static void Should_reject_ws_with_client_certificate_auth_if_PEM_cert_file_is_not_found()
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver("{{CertificateFilePath}}", "./cert.pem");
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(new Dictionary<string, bool>()
        {
            { "./cert.pem", false },
            { "./private_key.key", true }
        });
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pem, "{{CertificateFilePath}}", "./private_key.key", "prvkeypwd");
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{CertificateFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("./private_key.key"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("prvkeypwd"), Times.Once);
        Assert.Equal(TranslateRequestErrors.ClientCertificateFileNotFound, errorCode);
    }

    [Fact]
    public static void Should_reject_ws_with_client_certificate_auth_if_PEM_private_key_file_is_specified_and_not_found()
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver("{{PrivateKeyFilePath}}", "./private_key.key");
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(new Dictionary<string, bool>()
        {
            { "./cert.pem", true },
            { "./private_key.key", false }
        });
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pem, "./cert.pem", "{{PrivateKeyFilePath}}", "prvkeypwd");
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("./cert.pem"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{PrivateKeyFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("prvkeypwd"), Times.Once);
        Assert.Equal(TranslateRequestErrors.ClientCertificatePrivateKeyFileNotFound, errorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public static void Should_reject_ws_with_client_certificate_auth_if_PKCS12_without_password(string? filePassword)
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver("{{CertificateFilePath}}", "./cert.p12");
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pkcs12, "{{CertificateFilePath}}", null, filePassword);
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{CertificateFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates(null), Times.Exactly(2));
        Assert.Equal(TranslateRequestErrors.ClientCertificatePkcs12PasswordCannotBeBlank, errorCode);
    }

    [Fact]
    public static void Should_allow_req_with_client_certificate_auth_and_valid_PKCS12_params()
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new()
        {
            {"{{CertificateFilePath}}", "./cert.p12"},
            {"{{PrivateKeyFilePassword}}", "prvkeypwd"}
        });
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pkcs12, "{{CertificateFilePath}}", null, "{{PrivateKeyFilePassword}}");
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{CertificateFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{PrivateKeyFilePassword}}"), Times.Once);
        Assert.Null(errorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("my_password")]
    public static void Should_allow_req_with_client_certificate_auth_and_valid_PEM_params_separate_private_key_file(string filePassword)
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new()
        {
            {"{{CertificateFilePath}}", "./cert.pem"},
            {"{{PrivateKeyFilePath}}", "./private_key.key"},
            {"{{PrivateKeyFilePassword}}", filePassword}
        });
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pem, "{{CertificateFilePath}}", "{{PrivateKeyFilePath}}", "{{PrivateKeyFilePassword}}");
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{CertificateFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{PrivateKeyFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{PrivateKeyFilePassword}}"), Times.Once);
        Assert.Null(errorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("my_password")]
    public static void Should_allow_req_with_client_certificate_auth_and_valid_PEM_params_conjoined_private_key_file(string filePassword)
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new()
        {
            {"{{CertificateFilePath}}", "./cert.pem"},
            {"{{FilePassword}}", filePassword}
        });
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(true);
        PororocaRequestAuth auth = new();
        auth.SetClientCertificateAuth(PororocaRequestAuthClientCertificateType.Pem, "{{CertificateFilePath}}", null, "{{FilePassword}}");
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CustomAuth = auth;

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out var resolvedUri, out string? errorCode));

        // THEN
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{CertificateFilePath}}"), Times.Once);
        mockedVariableResolver.Verify(x => x.ReplaceTemplates("{{FilePassword}}"), Times.Once);
        Assert.Null(errorCode);
    }

    #endregion

    #region ARE VALID WEBSOCKET COMPRESSION OPTIONS

    [Fact]
    public static void Should_allow_websocket_connection_without_compression_options_specified()
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new());
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(false);
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out _, out string? errorCode));

        // THEN
        Assert.Null(errorCode);
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(12, 24)]
    [InlineData(50, 12)]
    [InlineData(-1, 99)]
    public static void Should_forbid_websocket_connection_with_compression_options_max_window_bits_out_of_range(int clientMaxWindowBits, int serverMaxWindowBits)
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new());
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(false);
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CompressionOptions = new(clientMaxWindowBits, true, serverMaxWindowBits, true);

        // WHEN
        Assert.False(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out _, out string? errorCode));

        // THEN
        Assert.Equal(TranslateRequestErrors.WebSocketCompressionMaxWindowBitsOutOfRange, errorCode);
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(15, 15)]
    [InlineData(10, 14)]
    [InlineData(12, 9)]
    public static void Should_allow_websocket_connection_with_compression_options_max_window_bits_within_range(int clientMaxWindowBits, int serverMaxWindowBits)
    {
        // GIVEN
        var mockedVariableResolver = MockVariableResolver(new());
        var mockedHttpVersionOSVerifier = MockHttpVersionOSVerifier(true, null);
        var mockedFileExistsVerifier = MockFileExistsVerifier(false);
        PororocaWebSocketConnection ws = new();
        ws.Url = "ws://www.pudim.com.br";
        ws.CompressionOptions = new(clientMaxWindowBits, true, serverMaxWindowBits, true);

        // WHEN
        Assert.True(IsValidConnection(mockedHttpVersionOSVerifier, mockedFileExistsVerifier, mockedVariableResolver.Object, ws, out _, out string? errorCode));

        // THEN
        Assert.Null(errorCode);
    }


    #endregion

}