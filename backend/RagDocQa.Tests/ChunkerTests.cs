using RagDocQa.Api.Ingest;

namespace RagDocQa.Tests;

public class ChunkerTests
{
    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"w{i}"));

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Blank_text_gives_no_chunks(string text) =>
        Assert.Empty(Chunker.Split(text));

    [Theory]
    [InlineData(1)]
    [InlineData(350)]
    public void Text_up_to_one_window_is_a_single_chunk(int count) =>
        Assert.Equal([Words(count)], Chunker.Split(Words(count)));

    [Fact]
    public void Consecutive_chunks_overlap_by_50_words()
    {
        var chunks = Chunker.Split(Words(351));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(350, chunks[0].Split(' ').Length);
        Assert.Equal(chunks[0].Split(' ')[^50..], chunks[1].Split(' ')[..50]);
    }

    [Fact]
    public void Every_word_lands_in_some_chunk_and_the_last_chunk_ends_the_text()
    {
        var chunks = Chunker.Split(Words(1000));

        var seen = chunks.SelectMany(c => c.Split(' ')).ToHashSet();
        Assert.Equal(1000, seen.Count);
        Assert.EndsWith("w999", chunks[^1]);
    }

    [Fact]
    public void Newlines_and_repeated_spaces_are_collapsed() =>
        Assert.Equal(["a b c"], Chunker.Split("a\n\n  b\tc  "));
}
