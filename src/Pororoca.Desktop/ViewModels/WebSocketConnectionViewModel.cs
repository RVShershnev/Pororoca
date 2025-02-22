using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive;
using System.Security.Authentication;
using AvaloniaEdit.Document;
using Pororoca.Desktop.ExportImport;
using Pororoca.Desktop.Localization;
using Pororoca.Desktop.Views;
using Pororoca.Domain.Features.Entities.Pororoca.WebSockets;
using Pororoca.Domain.Features.Requester;
using Pororoca.Domain.Features.TranslateRequest;
using Pororoca.Domain.Features.VariableResolution;
using Pororoca.Infrastructure.Features.Requester;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using static Pororoca.Domain.Features.Common.AvailablePororocaRequestSelectionOptions;
using static Pororoca.Domain.Features.TranslateRequest.WebSockets.ClientMessage.PororocaWebSocketClientMessageTranslator;
using static Pororoca.Domain.Features.TranslateRequest.WebSockets.ClientMessage.PororocaWebSocketClientMessageValidator;
using static Pororoca.Domain.Features.TranslateRequest.WebSockets.Connection.PororocaWebSocketConnectionTranslator;
using static Pororoca.Domain.Features.TranslateRequest.WebSockets.Connection.PororocaWebSocketConnectionValidator;

namespace Pororoca.Desktop.ViewModels;

public sealed class WebSocketConnectionViewModel : CollectionOrganizationItemParentViewModel<WebSocketClientMessageViewModel>
{
    #region COLLECTION ORGANIZATION

    public override Action OnAfterItemDeleted => Parent.OnAfterItemDeleted;
    public override Action<CollectionOrganizationItemViewModel> OnRenameSubItemSelected => Parent.OnRenameSubItemSelected;
    public ReactiveCommand<Unit, Unit> AddNewWebSocketClientMessageCmd { get; }
    public ReactiveCommand<Unit, Unit> PasteToWebSocketConnectionCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyCmd { get; }
    public ReactiveCommand<Unit, Unit> RenameCmd { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCmd { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }

    #endregion

    #region CONNECTION

    private readonly IPororocaVariableResolver varResolver;
    private readonly IPororocaHttpClientProvider httpClientProvider;
    private readonly PororocaWebSocketConnector connector;

    private bool isConnectedField;
    public bool IsConnected
    {
        get => this.isConnectedField;
        set
        {
            NameEditableTextBlockViewDataCtx.IsConnectedWebSocket = value;
            NameEditableTextBlockViewDataCtx.IsDisconnectedWebSocket = !value;
            this.RaiseAndSetIfChanged(ref this.isConnectedField, value);
        }
    }

    private bool isConnectingField;
    public bool IsConnecting
    {
        get => this.isConnectingField;
        set
        {
            this.RaiseAndSetIfChanged(ref this.isConnectingField, value);
            IsConnectingOrDisconnecting = this.isConnectingField || this.isDisconnectingField;
        }
    }

    private bool isDisconnectingField;
    public bool IsDisconnecting
    {
        get => this.isDisconnectingField;
        set
        {
            this.RaiseAndSetIfChanged(ref this.isDisconnectingField, value);
            IsConnectingOrDisconnecting = this.isConnectingField || this.isDisconnectingField;
        }
    }

    [Reactive]
    public bool IsConnectingOrDisconnecting { get; set; }

    [Reactive]
    public bool IsDisableTlsVerificationVisible { get; set; }

    public ReactiveCommand<Unit, Unit> ConnectCmd { get; }

    private CancellationTokenSource? cancelConnectionAttemptTokenSource;

    public ReactiveCommand<Unit, Unit> CancelConnectCmd { get; }

    public ReactiveCommand<Unit, Unit> DisconnectCmd { get; }

    private CancellationTokenSource? cancelDisconnectionAttemptTokenSource;

    public ReactiveCommand<Unit, Unit> CancelDisconnectCmd { get; }

    public ReactiveCommand<Unit, Unit> DisableTlsVerificationCmd { get; }

    // To preserve the state of the last shown request tab

    [Reactive]
    public int SelectedConnectionTabIndex { get; set; }

    [Reactive]
    public string Url { get; set; }

    [Reactive]
    public string ResolvedUrlToolTip { get; set; }

    public override ObservableCollection<WebSocketClientMessageViewModel> Items { get; } = new();

    #endregion

    #region CONNECTION REQUEST HTTP VERSION

    public ObservableCollection<string> HttpVersionSelectionOptions { get; }
    [Reactive]
    public int HttpVersionSelectedIndex { get; set; }
    private decimal HttpVersion =>
        AvailableHttpVersionsForWebSockets[HttpVersionSelectedIndex];

    #endregion

    #region REQUEST VALIDATION MESSAGE

    private string? invalidConnectionErrorCodeField;
    private string? InvalidConnectionErrorCode
    {
        get => this.invalidConnectionErrorCodeField;
        set
        {
            this.invalidConnectionErrorCodeField = value;
            IsInvalidConnectionErrorVisible = value is not null;
            InvalidConnectionError = value switch
            {
                TranslateRequestErrors.ClientCertificateFileNotFound => Localizer.Instance["RequestValidation/ClientCertificateFileNotFound"],
                TranslateRequestErrors.ClientCertificatePkcs12PasswordCannotBeBlank => Localizer.Instance["RequestValidation/ClientCertificatePkcs12PasswordCannotBeBlank"],
                TranslateRequestErrors.ClientCertificatePrivateKeyFileNotFound => Localizer.Instance["RequestValidation/ClientCertificatePrivateKeyFileNotFound"],
                TranslateRequestErrors.InvalidUrl => Localizer.Instance["RequestValidation/InvalidUrl"],
                TranslateRequestErrors.Http2UnavailableInOSVersion => Localizer.Instance["RequestValidation/Http2Unavailable"],
                TranslateRequestErrors.WebSocketHttpVersionUnavailable => Localizer.Instance["RequestValidation/WebSocketHttpVersionUnavailable"],
                TranslateRequestErrors.WebSocketCompressionMaxWindowBitsOutOfRange => Localizer.Instance["RequestValidation/WebSocketCompressionMaxWindowBitsOutOfRange"],
                TranslateRequestErrors.WebSocketUnknownConnectionTranslationError => Localizer.Instance["RequestValidation/WebSocketUnknownConnectionTranslationError"],
                _ => Localizer.Instance["RequestValidation/WebSocketUnknownConnectionTranslationError"]
            };
        }
    }

    [Reactive]
    public bool IsInvalidConnectionErrorVisible { get; set; }
    [Reactive]
    public string? InvalidConnectionError { get; set; }

    #endregion

    #region CONNECTION OPTIONS

    [Reactive]
    public int ConnectionOptionSelectedIndex { get; set; }

    [Reactive]
    public bool IsConnectionOptionHeadersSelected { get; set; }

    [Reactive]
    public bool IsConnectionOptionSubprotocolsSelected { get; set; }

    [Reactive]
    public bool IsConnectionOptionCompressionSelected { get; set; }

    #region CONNECTION OPTION HEADERS

    public ObservableCollection<KeyValueParamViewModel> ConnectionRequestHeaders { get; }
    public ReactiveCommand<Unit, Unit> AddNewConnectionRequestHeaderCmd { get; }

    #endregion

    #region CONNECTION OPTION SUBPROTOCOLS

    public ObservableCollection<KeyValueParamViewModel> Subprotocols { get; }
    public ReactiveCommand<Unit, Unit> AddNewSubprotocolCmd { get; }

    #endregion

    #region CONNECTION OPTION COMPRESSION

    [Reactive]
    public bool EnableCompression { get; set; }

    [Reactive]
    public bool CompressionClientContextTakeoverEnabled { get; set; }

    [Reactive]
    public int CompressionClientMaxWindowBits { get; set; }

    [Reactive]
    public bool CompressionServerContextTakeoverEnabled { get; set; }

    [Reactive]
    public int CompressionServerMaxWindowBits { get; set; }

    #endregion

    #endregion

    #region CONNECTION REQUEST AUTH

    [Reactive]
    public RequestAuthViewModel RequestAuthDataCtx { get; set; }

    #endregion

    #region CONNECTION EXCEPTION

    [Reactive]
    public string? ConnectionExceptionContent { get; set; }

    #endregion

    #region EXCHANGED MESSAGES

    private string? invalidClientMessageErrorCodeField;
    private string? InvalidClientMessageErrorCode
    {
        get => this.invalidClientMessageErrorCodeField;
        set
        {
            this.invalidClientMessageErrorCodeField = value;
            IsInvalidClientMessageErrorVisible = value is not null;
            InvalidClientMessageError = value switch
            {
                TranslateRequestErrors.WebSocketNotConnected => Localizer.Instance["RequestValidation/WebSocketNotConnected"],
                TranslateRequestErrors.WebSocketClientMessageContentFileNotFound => Localizer.Instance["RequestValidation/WebSocketClientMessageContentFileNotFound"],
                TranslateRequestErrors.WebSocketUnknownClientMessageTranslationError => Localizer.Instance["RequestValidation/WebSocketUnknownClientMessageTranslationError"],
                _ => Localizer.Instance["RequestValidation/WebSocketUnknownClientMessageTranslationError"]
            };
        }
    }

    [Reactive]
    public bool IsInvalidClientMessageErrorVisible { get; set; }
    [Reactive]
    public string? InvalidClientMessageError { get; set; }

    [Reactive]
    public bool IsSendingAMessage { get; set; }

    public ObservableCollection<WebSocketExchangedMessageViewModel> ExchangedMessages { get; }

    [Reactive]
    public int MessageToSendSelectedIndex { get; set; }

    public ReactiveCommand<Unit, Unit> SendMessageCmd { get; }

    #endregion

    #region MESSAGE DETAIL

    [Reactive]
    public string? SelectedExchangedMessageType { get; set; }

    [Reactive]
    public TextDocument? SelectedExchangedMessageContentTextDocument { get; set; }

    public string? SelectedExchangedMessageContent
    {
        get => SelectedExchangedMessageContentTextDocument?.Text;
        set => SelectedExchangedMessageContentTextDocument = new(value ?? string.Empty);
    }

    private WebSocketExchangedMessageViewModel? selectedExchangedMessageField;
    public WebSocketExchangedMessageViewModel? SelectedExchangedMessage
    {
        get => this.selectedExchangedMessageField;
        set
        {
            this.RaiseAndSetIfChanged(ref this.selectedExchangedMessageField, value);

            if (value is not null)
            {
                OnSelectedExchangedMessageChanged(value);
            }
        }
    }

    public bool IsSelectedExchangedMessageContentJson { get; set; }

    [Reactive]
    public bool IsSaveSelectedExchangedMessageToFileVisible { get; set; }

    public ReactiveCommand<Unit, Unit> SaveSelectedExchangedMessageToFileCmd { get; }

    #endregion

    public WebSocketConnectionViewModel(ICollectionOrganizationItemParentViewModel parentVm,
                                        IPororocaVariableResolver variableResolver,
                                        PororocaWebSocketConnection ws) : base(parentVm, ws.Name)
    {
        #region COLLECTION ORGANIZATION
        Localizer.Instance.SubscribeToLanguageChange(OnLanguageChanged);

        NameEditableTextBlockViewDataCtx.IsDisconnectedWebSocket = true;
        AddNewWebSocketClientMessageCmd = ReactiveCommand.Create(AddNewWebSocketClientMessage);
        PasteToWebSocketConnectionCmd = ReactiveCommand.Create(PasteToThis);
        CopyCmd = ReactiveCommand.Create(Copy);
        RenameCmd = ReactiveCommand.Create(RenameThis);
        MoveUpCmd = ReactiveCommand.Create(MoveThisUp);
        MoveDownCmd = ReactiveCommand.Create(MoveThisDown);
        DeleteCmd = ReactiveCommand.Create(Delete);
        #endregion

        #region CONNECTION

        this.varResolver = variableResolver;
        this.httpClientProvider = PororocaHttpClientProvider.Singleton;
        this.connector = new(OnWebSocketConnectionChanged, OnWebSocketMessageSending);

        if (ws.ClientMessages is not null)
        {
            foreach (var wsReqMsg in ws.ClientMessages)
                Items.Add(new(this, wsReqMsg));

            RefreshSubItemsAvailableMovements();
        }

        ConnectCmd = ReactiveCommand.CreateFromTask(ConnectAsync);
        CancelConnectCmd = ReactiveCommand.Create(CancelConnect);
        DisconnectCmd = ReactiveCommand.CreateFromTask(DisconnectAsync);
        CancelDisconnectCmd = ReactiveCommand.Create(CancelDisconnect);
        DisableTlsVerificationCmd = ReactiveCommand.Create(DisableTlsVerification);

        #endregion

        #region CONNECTION REQUEST HTTP VERSION AND URL
        ResolvedUrlToolTip = Url = ws.Url;

        HttpVersionSelectionOptions = new(AvailableHttpVersionsForWebSockets.Select(FormatHttpVersionString));
        int httpVersionSelectionIndex = HttpVersionSelectionOptions.IndexOf(FormatHttpVersionString(ws.HttpVersion));
        HttpVersionSelectedIndex = httpVersionSelectionIndex >= 0 ? httpVersionSelectionIndex : 0;
        #endregion

        #region CONNECTION REQUEST AUTH
        RequestAuthDataCtx = new(ws.CustomAuth);
        #endregion

        #region CONNECTION OPTIONS

        ConnectionOptionSelectedIndex = 0;
        IsConnectionOptionHeadersSelected = true;
        IsConnectionOptionSubprotocolsSelected = false;
        IsConnectionOptionCompressionSelected = false;

        #region CONNECTION OPTION HEADERS

        ConnectionRequestHeaders = new();
        if (ws.Headers is not null)
        {
            foreach (var h in ws.Headers)
            {
                ConnectionRequestHeaders.Add(new(ConnectionRequestHeaders, h));
            }
        }
        AddNewConnectionRequestHeaderCmd = ReactiveCommand.Create(AddNewConnectionRequestHeader);

        #endregion

        #region CONNECTION OPTION SUBPROTOCOLS

        Subprotocols = new();
        if (ws.Subprotocols is not null)
        {
            foreach (var s in ws.Subprotocols)
            {
                Subprotocols.Add(new(Subprotocols, s));
            }
        }
        AddNewSubprotocolCmd = ReactiveCommand.Create(AddNewSubprotocol);

        #endregion

        #region CONNECTION OPTION COMPRESSION

        if (ws.EnableCompression)
        {
            EnableCompression = true;
            CompressionClientContextTakeoverEnabled = ws.CompressionOptions!.ClientContextTakeover;
            CompressionClientMaxWindowBits = ws.CompressionOptions!.ClientMaxWindowBits;
            CompressionServerContextTakeoverEnabled = ws.CompressionOptions!.ServerContextTakeover;
            CompressionServerMaxWindowBits = ws.CompressionOptions!.ServerMaxWindowBits;
        }
        else
        {
            EnableCompression = false;
            CompressionClientContextTakeoverEnabled = PororocaWebSocketCompressionOptions.DefaultClientContextTakeover;
            CompressionClientMaxWindowBits = PororocaWebSocketCompressionOptions.DefaultClientMaxWindowBits;
            CompressionServerContextTakeoverEnabled = PororocaWebSocketCompressionOptions.DefaultServerContextTakeover;
            CompressionServerMaxWindowBits = PororocaWebSocketCompressionOptions.DefaultServerMaxWindowBits;
        }

        #endregion

        #endregion

        #region EXCHANGED MESSAGES
        ExchangedMessages = new();
        this.connector.ExchangedMessages.CollectionChanged += OnConnectorExchangedMessagesUpdated;
        SendMessageCmd = ReactiveCommand.CreateFromTask(SendMessageAsync);
        #endregion

        #region MESSAGE DETAIL
        // we need to always set SelectedExchangedMessageContent with a value, even if it is null,
        // to initialize with a TextDocument object
        SelectedExchangedMessageContent = null;
        SaveSelectedExchangedMessageToFileCmd = ReactiveCommand.CreateFromTask(SaveSelectedExchangedMessageToFileAsync);
        #endregion
    }

    #region COLLECTION ORGANIZATION

    public override void RefreshSubItemsAvailableMovements()
    {
        for (int x = 0; x < Items.Count; x++)
        {
            var colItemVm = Items[x];
            bool canMoveUp = x > 0;
            bool canMoveDown = x < Items.Count - 1;
            colItemVm.CanMoveUp = canMoveUp;
            colItemVm.CanMoveDown = canMoveDown;
        }
    }

    protected override void CopyThis() =>
        CollectionsGroupDataCtx.PushToCopy(ToWebSocketConnection());

    public override void PasteToThis()
    {
        var itemsToPaste = CollectionsGroupDataCtx.FetchCopiesOfWebSocketClientMessages();
        foreach (var itemToPaste in itemsToPaste)
        {
            AddWebSocketClientMessage(itemToPaste);
        }
    }

    private void AddNewWebSocketClientMessage()
    {
        PororocaWebSocketClientMessage wsReqMsg = new(PororocaWebSocketMessageType.Text,
                                                       Localizer.Instance["WebSocketClientMessage/NewMessage"],
                                                       PororocaWebSocketClientMessageContentMode.Raw,
                                                       string.Empty,
                                                       PororocaWebSocketMessageRawContentSyntax.Json,
                                                       null,
                                                       false);
        AddWebSocketClientMessage(wsReqMsg, showItemInScreen: true);
    }

    public void AddWebSocketClientMessage(PororocaWebSocketClientMessage wsReqMsgToAdd, bool showItemInScreen = false)
    {
        WebSocketClientMessageViewModel wsReqMsgToAddVm = new(this, wsReqMsgToAdd);

        Items.Add(wsReqMsgToAddVm);
        IsExpanded = true;
        RefreshSubItemsAvailableMovements();
        SetAsItemInFocus(wsReqMsgToAddVm, showItemInScreen);
    }

    private void OnLanguageChanged()
    {
        if (InvalidConnectionErrorCode is not null)
        {
            string code = InvalidConnectionErrorCode;
            InvalidConnectionErrorCode = code; // this will trigger an update
        }

        if (InvalidClientMessageErrorCode is not null)
        {
            string code = InvalidClientMessageErrorCode;
            InvalidClientMessageErrorCode = code; // this will trigger an update
        }
    }

    #endregion

    #region REQUEST HTTP METHOD, HTTP VERSION AND URL

    public void UpdateResolvedUrlToolTip() =>
        ResolvedUrlToolTip = this.varResolver.ReplaceTemplates(Url);

    private static string FormatHttpVersionString(decimal httpVersion) =>
        httpVersion switch
        {
            1.0m => "HTTP/1.0",
            1.1m => "HTTP/1.1",
            2.0m => "HTTP/2",
            3.0m => "HTTP/3",
            _ => string.Format(CultureInfo.InvariantCulture, "HTTP/{0:0.0}", httpVersion)
        };

    #endregion

    #region CONNECTION REQUEST HEADERS

    private void AddNewConnectionRequestHeader() =>
        ConnectionRequestHeaders.Add(new(ConnectionRequestHeaders, true, string.Empty, string.Empty));

    #endregion

    #region CONNECTION OPTION SUBPROTOCOLS

    private void AddNewSubprotocol() =>
        Subprotocols.Add(new(Subprotocols, true, string.Empty, string.Empty));

    #endregion

    #region CONVERT VIEW INPUTS TO REQUEST ENTITY

    private PororocaWebSocketCompressionOptions? WrapCompressionOptionsFromInputs() =>
        EnableCompression ?
            new(CompressionClientMaxWindowBits,
                CompressionClientContextTakeoverEnabled,
                CompressionServerMaxWindowBits,
                CompressionServerContextTakeoverEnabled) :
            null;

    public PororocaWebSocketConnection ToWebSocketConnection()
    {
        PororocaWebSocketConnection newWs = new(Name);
        UpdateConnectionWithInputs(newWs);
        return newWs;
    }

    private void UpdateConnectionWithInputs(PororocaWebSocketConnection ws)
    {
        ws.UpdateName(Name);
        ws.HttpVersion = HttpVersion;
        ws.Url = Url;
        ws.CustomAuth = RequestAuthDataCtx.ToCustomAuth();
        ws.Headers = ConnectionRequestHeaders.Count == 0 ? null : ConnectionRequestHeaders.Select(h => h.ToKeyValueParam()).ToList();
        ws.ClientMessages = Items.Count == 0 ? null : Items.Select(i => i.ToWebSocketClientMessage()).ToList();
        ws.Subprotocols = Subprotocols.Count == 0 ? null : Subprotocols.Select(s => s.ToKeyValueParam()).ToList();
        ws.CompressionOptions = WrapCompressionOptionsFromInputs();
    }

    #endregion

    #region CONNECT / DISCONNECT

    private void OnWebSocketConnectionChanged(PororocaWebSocketConnectorState state, Exception? ex)
    {
        IsConnecting = state == PororocaWebSocketConnectorState.Connecting;
        IsDisconnecting = state == PororocaWebSocketConnectorState.Disconnecting;
        IsConnected = state == PororocaWebSocketConnectorState.Connected;
        ConnectionExceptionContent = ex?.ToString();
        if (ex is not null)
        {
            // switching to Exception tab
            // TODO: do not use fixed integers here, find a better way
            SelectedConnectionTabIndex = 2;
        }
        IsDisableTlsVerificationVisible = IsInvalidTlsCertificateWsException(ex);
    }

    private static bool IsInvalidTlsCertificateWsException(Exception? ex) =>
        ex?.InnerException?.InnerException is AuthenticationException aex
     && aex.Message.Contains("remote certificate is invalid", StringComparison.InvariantCultureIgnoreCase);

    private void OnWebSocketMessageSending(bool isSendingAMessage) =>
        IsSendingAMessage = isSendingAMessage;

    private async Task ConnectAsync()
    {
        var wsConn = ToWebSocketConnection();
        bool disableTlsVerification = ((MainWindowViewModel)MainWindow.Instance!.DataContext!).IsSslVerificationDisabled;

        if (!IsValidConnection(this.varResolver, wsConn, out var resolvedUri, out string? translateUriErrorCode))
        {
            InvalidConnectionErrorCode = translateUriErrorCode;
        }
        else if (!TryTranslateConnection(this.varResolver, this.httpClientProvider, wsConn, disableTlsVerification,
                                         out var resolvedClients, out string? translateConnErrorCode))
        {
            InvalidConnectionErrorCode = translateConnErrorCode;
        }
        else
        {
            InvalidConnectionErrorCode = null;
            this.cancelConnectionAttemptTokenSource = new();
            // This needs to be done in a different thread.
            // Awaiting the request.RequestAsync() here, or simply returning its Task,
            // causes the UI to freeze for a few seconds, especially when performing the first request to a server.
            // That is why we are invoking the code to run in a new thread, like below.
            await Task.Run(() => this.connector.ConnectAsync(resolvedClients.wsCli!, resolvedClients.httpCli!, resolvedUri!, this.cancelConnectionAttemptTokenSource.Token));
        }
    }

    private void CancelConnect() =>
        this.cancelConnectionAttemptTokenSource?.Cancel();

    private Task DisconnectAsync()
    {
        this.cancelDisconnectionAttemptTokenSource = new();
        // This needs to be done in a different thread.
        // Awaiting the request.RequestAsync() here, or simply returning its Task,
        // causes the UI to freeze for a few seconds, especially when performing the first request to a server.
        // That is why we are invoking the code to run in a new thread, like below.
        return Task.Run(() => this.connector.DisconnectAsync(this.cancelDisconnectionAttemptTokenSource.Token));
    }

    private void CancelDisconnect() =>
        this.cancelDisconnectionAttemptTokenSource?.Cancel();

    private void DisableTlsVerification()
    {
        ((MainWindowViewModel)MainWindow.Instance!.DataContext!).IsSslVerificationDisabled = true;
        IsDisableTlsVerificationVisible = false;
    }

    #endregion

    #region EXCHANGED MESSAGES

    private async Task SendMessageAsync()
    {
        if (!IsConnected)
        {
            InvalidClientMessageErrorCode = TranslateRequestErrors.WebSocketNotConnected;
        }
        else
        {
            var msg = Items[MessageToSendSelectedIndex].ToWebSocketClientMessage();

            if (!IsValidClientMessage(this.varResolver, msg, out string? validationErrorCode))
            {
                InvalidClientMessageErrorCode = validationErrorCode;
            }
            else if (!TryTranslateClientMessage(this.varResolver, msg, out var resolvedMsgToSend, out string? translationErrorCode))
            {
                InvalidClientMessageErrorCode = translationErrorCode;
            }
            else
            {
                InvalidClientMessageErrorCode = null;
                await this.connector.SendMessageAsync(resolvedMsgToSend!);
            }
        }
    }

    private void OnConnectorExchangedMessagesUpdated(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ExchangedMessages.Clear();
        }
        else if (e.Action == NotifyCollectionChangedAction.Add)
        {
            foreach (object newItem in e.NewItems!)
            {
                var msg = (PororocaWebSocketMessage)newItem;
                ExchangedMessages.Insert(0, new(msg));
            }
        }
    }

    #endregion

    #region MESSAGE DETAIL

    private void OnSelectedExchangedMessageChanged(WebSocketExchangedMessageViewModel vm)
    {
        SelectedExchangedMessageType = vm.TypeDescription;
        SelectedExchangedMessageContent = vm.TextContent;
        IsSaveSelectedExchangedMessageToFileVisible = vm.CanBeSavedToFile;
    }

    private async Task SaveSelectedExchangedMessageToFileAsync()
    {
        static string GenerateDefaultInitialFileName(WebSocketExchangedMessageViewModel vm)
        {
            string fileExtensionWithoutDot = vm.IsJsonTextContent ? "json" :
                                             vm.Type == PororocaWebSocketMessageType.Text ? "txt" :
                                             string.Empty;

            return $"websocket-msg-{vm.ShortInstantDescription}.{fileExtensionWithoutDot}";
        }

        if (SelectedExchangedMessage is not null && SelectedExchangedMessage.CanBeSavedToFile)
        {
            string initialFileName = GenerateDefaultInitialFileName(SelectedExchangedMessage);

            string? saveFileOutputPath = await FileExporterImporter.SelectPathForFileToBeSavedAsync(initialFileName);
            if (saveFileOutputPath != null)
            {
                const int fileStreamBufferSize = 4096;
                using FileStream fs = new(saveFileOutputPath, FileMode.Create, FileAccess.Write, FileShare.None, fileStreamBufferSize, useAsync: true);
                await fs.WriteAsync((Memory<byte>)SelectedExchangedMessage.Bytes!);
            }
        }
    }

    #endregion
}