namespace Leitweb.Api.Domain;

public sealed class AddressEntry
{
    public long Id { get; set; }
    public string Municipality { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string DisplayName => $"{Street} {HouseNumber}, {PostalCode} {Municipality}".Trim();
}
