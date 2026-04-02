using System;
using System.Collections.Generic;
using System.Text;

namespace Gallery2.Models;

public record FaceRect(int X, int Y, int Width, int Height);

public record EmbeddingData(
    string FilePath,
    FaceRect BoundingBox,
    float[] Embedding
);

public record ClusterData(
    float[] Centroid,
    string RepresentativeFilePath,
    FaceRect RepresentativeBoundingBox,
    string? Name
);

public record ClusterState(
      float[] Centroid,
      int Count,
      string RepFilePath,
      FaceRect RepBoundingBox);

public record FaceIndexingProgress(
    int ProcessedFiles,
    int TotalFiles,
    int Step,
    string Title
);