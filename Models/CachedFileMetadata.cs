using System;
using System.Collections.Generic;
using System.Text;

namespace Gallery2.Models;

public record CachedFileMetadata(
    string FilePath,
    DateTime? DateTaken,
    double? Latitude,
    double? Longitude);