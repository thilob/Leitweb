param([string]$OutputPath = "src/Leitweb.Api/Data/addresses.tsv")
$ErrorActionPreference = "Stop"
$ue = [char]0x00FC
$municipalities = @("Wermelskirchen", "Remscheid", ("H"+$ue+"ckeswagen"), ("Wipperf"+$ue+"rth"), ("K"+$ue+"rten"), "Odenthal", "Burscheid", "Solingen")
$endpoint = "https://ogc-api.nrw.de/gebref/v1/collections/gebref/items"
$rows = [System.Collections.Generic.List[string]]::new()
$rows.Add("municipality`tpostalCode`tstreet`thouseNumber`tlatitude`tlongitude")

foreach ($municipality in $municipalities) {
    Write-Host "Importiere $municipality ..."
    $offset = 0
    do {
        $uri = $endpoint + "?gmd=" + [uri]::EscapeDataString($municipality) + "&limit=10000&offset=" + $offset
        $webResponse = Invoke-WebRequest -UseBasicParsing -Method Get -Uri $uri -TimeoutSec 180 -Headers @{ "User-Agent" = "Dorfpolizei-Well-Address-Importer/1.0"; "Accept" = "application/geo+json" }
        $response = [Text.Encoding]::UTF8.GetString($webResponse.RawContentStream.ToArray()) | ConvertFrom-Json
        foreach ($feature in $response.features) {
            $properties = $feature.properties
            $houseNumber = ([string]$properties.hnr + [string]$properties.adz).Trim()
            if ([string]::IsNullOrWhiteSpace($properties.str) -or [string]::IsNullOrWhiteSpace($houseNumber)) { continue }
            $longitude = $feature.geometry.coordinates[0].ToString([Globalization.CultureInfo]::InvariantCulture)
            $latitude = $feature.geometry.coordinates[1].ToString([Globalization.CultureInfo]::InvariantCulture)
            $rows.Add("$municipality`t`t$($properties.str)`t$houseNumber`t$latitude`t$longitude")
        }
        $offset += [int]$response.numberReturned
    } while ([int]$response.numberReturned -gt 0 -and $offset -lt [int]$response.numberMatched)
}

$header = $rows[0]
$unique = $rows | Select-Object -Skip 1 | Sort-Object -Unique
if ($unique.Count -eq 0) { throw "Der Import lieferte keine Adressen; die bestehende Datei bleibt unverändert." }
$target = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.File]::WriteAllLines($target, @($header) + $unique, [System.Text.UTF8Encoding]::new($false))
Write-Host "$($unique.Count) amtliche Adressen nach $target geschrieben. Quelle: Geobasis NRW, Datenlizenz Deutschland Zero 2.0."
