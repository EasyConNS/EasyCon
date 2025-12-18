namespace EasyCapture;

public sealed class ECSearch
{
    public static IEnumerable<SearchMethod> GetEnableSearchMethods()
    {
        return
        [
            SearchMethod.SqDiffNormed,
            SearchMethod.CCorrNormed,
            SearchMethod.CCoeffNormed,
            SearchMethod.EdgeDetectXY,
            SearchMethod.EdgeDetectLaplacian,
        ];
    }
}
