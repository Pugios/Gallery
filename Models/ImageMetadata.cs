using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace app.Models
{
    public class ImageMetadata
    {
        public string FilePath { get; set; }
        public DateTime? DateTime { get; set; }
        public GpsCoordinates? Location { get; set; }
        public double? ImageDirection { get; set; }
        public bool IsMagneticDirection { get; set; }

        public string GetDisplayDate()
        {
            return DateTime?.ToString("D", CultureInfo.InvariantCulture) ?? "No Date Found";
        }

    }

    public class GpsCoordinates
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
