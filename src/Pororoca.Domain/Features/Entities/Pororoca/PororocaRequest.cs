using System.Text.Json.Serialization;

namespace Pororoca.Domain.Features.Entities.Pororoca;

public abstract class PororocaRequest : PororocaCollectionItem
{
    [JsonPropertyOrder(-3)]
    public PororocaRequestType RequestType { get; init; }

    protected PororocaRequest(PororocaRequestType reqType, string name) : base(name) =>
        RequestType = reqType;
}