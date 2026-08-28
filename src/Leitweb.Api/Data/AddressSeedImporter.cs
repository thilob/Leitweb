using System.Globalization;
using Leitweb.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Data;

public static class AddressSeedImporter
{
    public static async Task ImportIfEmptyAsync(LeitwebDbContext db, string path, CancellationToken ct = default)
    {
        if (!File.Exists(path) || await db.Addresses.AnyAsync(ct)) return;
        var batch = new List<AddressEntry>(1000);
        var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var columns = line.Split('\t');
            if (columns.Length < 6 || string.IsNullOrWhiteSpace(columns[2]) || string.IsNullOrWhiteSpace(columns[3])) continue;
            if (!importedKeys.Add($"{columns[0]}\u001f{columns[2]}\u001f{columns[3]}")) continue;
            batch.Add(new AddressEntry
            {
                Municipality = columns[0], PostalCode = columns[1], Street = columns[2], HouseNumber = columns[3],
                Latitude = double.TryParse(columns[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ? lat : null,
                Longitude = double.TryParse(columns[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ? lon : null
            });
            if (batch.Count < 1000) continue;
            db.Addresses.AddRange(batch); await db.SaveChangesAsync(ct); batch.Clear(); db.ChangeTracker.Clear();
        }
        if (batch.Count > 0) { db.Addresses.AddRange(batch); await db.SaveChangesAsync(ct); }
    }
}
