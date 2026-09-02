using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace RagDocQa.Api.Search;

public static class SearchSetup
{
    public const string IndexName = "doc-chunks";

    // Must match the output dimensions we request from Gemini.
    // 768 instead of the 3072 default keeps us inside the 50 MB free tier.
    public const int Dimensions = 768;

    public static async Task EnsureIndexAsync(string endpoint, string apiKey)
    {
        var client = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

        var index = new SearchIndex(IndexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("fileName", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsFilterable = true },
                new SearchableField("content"),
                new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = Dimensions,
                    VectorSearchProfileName = "hnsw-profile"
                }
            },
            VectorSearch = new VectorSearch
            {
                Profiles   = { new VectorSearchProfile("hnsw-profile", "hnsw-config") },
                Algorithms = { new HnswAlgorithmConfiguration("hnsw-config") }
            }
        };

        await client.CreateOrUpdateIndexAsync(index);
    }
}
