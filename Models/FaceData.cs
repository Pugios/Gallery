using System;
using System.Collections.Generic;
using System.Text;
namespace Gallery2.Models;

public record FaceRect(int X, int Y, int Width, int Height);

public record EmbeddingData(
    string FilePath,
    Rect BoundingBox,
    float[] Embedding,
    float Confidence
);

public record ClusterData(
    float[] Centroid,
    string RepresentativeFilePath,
    Rect RepresentativeBoundingBox,
    string? Name
);

public record FaceIndexingProgress(
    int ProcessedFiles,
    int TotalFiles,
    string Title
);