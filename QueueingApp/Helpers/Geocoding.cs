using System.Net;
using Newtonsoft.Json.Linq;

namespace QueueingApp.Helpers
{
    public static class Geocoding
    {
        // postcodes.io: free, no API key, UK postcodes only. Returns the postcode centroid.
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        public static string NormalisePostcode(string? postcode)
        {
            string compact = new string((postcode ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (compact.Length < 5 || compact.Length > 7)
            {
                return compact;
            }
            return compact.Substring(0, compact.Length - 3) + " " + compact.Substring(compact.Length - 3);
        }

        /// <summary>Null when the postcode does not exist. Throws when postcodes.io cannot be reached.</summary>
        public static async Task<(decimal Latitude, decimal Longitude)?> LookupPostcodeAsync(string postcode)
        {
            string normalised = NormalisePostcode(postcode);
            if (string.IsNullOrWhiteSpace(normalised))
            {
                return null;
            }

            using HttpResponseMessage res = await _http.GetAsync("https://api.postcodes.io/postcodes/" + Uri.EscapeDataString(normalised));
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            res.EnsureSuccessStatusCode();

            JObject json = JObject.Parse(await res.Content.ReadAsStringAsync());
            JToken? result = json["result"];
            JToken? latToken = result?["latitude"];
            JToken? lngToken = result?["longitude"];
            if (latToken == null || lngToken == null || latToken.Type == JTokenType.Null || lngToken.Type == JTokenType.Null)
            {
                // Some postcodes (e.g. Channel Islands, PO boxes) exist but have no coordinates.
                return null;
            }
            decimal lat = Math.Round((decimal)latToken, 6);
            decimal lng = Math.Round((decimal)lngToken, 6);
            return (lat, lng);
        }

        /// <summary>Great-circle distance in metres (haversine).</summary>
        public static double DistanceMetres(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371000;
            double dLat = ToRad(lat2 - lat1);
            double dLng = ToRad(lng2 - lng1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public static bool IsValidCoordinate(decimal? latitude, decimal? longitude)
        {
            return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 &&
                   !(latitude == 0 && longitude == 0);
        }

        private static double ToRad(double deg) => deg * Math.PI / 180;
    }
}
