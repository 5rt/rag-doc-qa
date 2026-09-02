namespace RagDocQa.Api.Ingest;

public static class Chunker
{
    /// <summary>
    /// Splits text into overlapping windows of words. The overlap matters:
    /// without it, a sentence straddling a boundary gets cut in half and
    /// neither piece answers the question properly.
    /// </summary>
    public static List<string> Split(string text, int wordsPerChunk = 350, int overlap = 50)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();

        for (var i = 0; i < words.Length; i += wordsPerChunk - overlap)
        {
            var take = Math.Min(wordsPerChunk, words.Length - i);
            chunks.Add(string.Join(' ', words.Skip(i).Take(take)));
            if (i + take >= words.Length) break;
        }

        return chunks;
    }
}
