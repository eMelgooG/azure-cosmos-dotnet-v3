// ----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ----------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Performance.Tests.Json
{
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using BenchmarkDotNet.Attributes;
    using BenchmarkDotNet.Configs;
    using BenchmarkDotNet.Jobs;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Tests.Json;

    /// <summary>
    /// A/B micro-benchmark isolating the read-path change in PR #5908 for
    /// <c>CosmosSystemTextJsonSerializer</c>.
    ///
    /// Binary path:
    ///   OLD = System.Text.Json.JsonSerializer.Deserialize&lt;T&gt;(cosmosObject.ToString(), options)   // full UTF-16 string alloc
    ///   NEW = cosmosObject.WriteTo(JsonWriter Text) + Deserialize&lt;T&gt;(ReadOnlySpan&lt;byte&gt;, options)
    ///
    /// Text path (DeserializeStream):
    ///   OLD = new StreamReader(stream).ReadToEnd() + Deserialize&lt;T&gt;(string, options)
    ///   NEW = Deserialize&lt;T&gt;(Stream, options)
    /// </summary>
    [Config(typeof(MediumRunConfig))]
    [MemoryDiagnoser]
    public class StjSerializerReadBenchmark
    {
        private class MediumRunConfig : ManualConfig
        {
            public MediumRunConfig()
            {
                this.AddJob(Job.MediumRun);
            }
        }

        private static readonly JsonSerializerOptions Options = new ()
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Number of embedded documents. Drives payload size so the large-document
        /// (LOH) allocation behaviour of the OLD string path is visible.
        /// </summary>
        [Params(1, 100, 1000)]
        public int DocumentCount;

        private byte[] textUtf8;
        private CosmosObject cosmosObject;

        [GlobalSetup]
        public void Setup()
        {
            string unit = File.ReadAllText("samplepayload.json");

            StringBuilder sb = new ();
            sb.Append("{\"id\":\"root\",\"items\":[");
            for (int i = 0; i < this.DocumentCount; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(unit);
            }

            sb.Append("]}");
            string json = sb.ToString();

            this.textUtf8 = Encoding.UTF8.GetBytes(json);

            byte[] binaryBuffer = JsonTestUtils.ConvertTextToBinary(json);
            this.cosmosObject = CosmosObject.CreateFromBuffer(binaryBuffer);
        }

        [Benchmark(Description = "Binary_Old (ToString -> Deserialize<string>)")]
        [BenchmarkCategory("Binary")]
        public object Binary_Old()
        {
            string text = this.cosmosObject.ToString();
            return System.Text.Json.JsonSerializer.Deserialize<FamilyRoot>(text, Options);
        }

        [Benchmark(Description = "Binary_New (WriteTo -> Deserialize<ReadOnlySpan<byte>>)", Baseline = false)]
        [BenchmarkCategory("Binary")]
        public object Binary_New()
        {
            IJsonWriter jsonWriter = JsonWriter.Create(JsonSerializationFormat.Text);
            this.cosmosObject.WriteTo(jsonWriter);
            return System.Text.Json.JsonSerializer.Deserialize<FamilyRoot>(jsonWriter.GetResult().Span, Options);
        }

        [Benchmark(Description = "Text_Old (ReadToEnd -> Deserialize<string>)")]
        [BenchmarkCategory("Text")]
        public object Text_Old()
        {
            using MemoryStream stream = new (this.textUtf8, writable: false);
            using StreamReader reader = new (stream);
            return System.Text.Json.JsonSerializer.Deserialize<FamilyRoot>(reader.ReadToEnd(), Options);
        }

        [Benchmark(Description = "Text_New (Deserialize<Stream>)")]
        [BenchmarkCategory("Text")]
        public object Text_New()
        {
            using MemoryStream stream = new (this.textUtf8, writable: false);
            return System.Text.Json.JsonSerializer.Deserialize<FamilyRoot>(stream, Options);
        }

        private class FamilyRoot
        {
            public string Id { get; set; }

            public Family[] Items { get; set; }
        }

        private class Family
        {
            public string Id { get; set; }

            public string LastName { get; set; }

            public Parent[] Parents { get; set; }

            public Child[] Children { get; set; }

            public Location Location { get; set; }

            public bool IsRegistered { get; set; }
        }

        private class Parent
        {
            public string FirstName { get; set; }

            public string Relationship { get; set; }
        }

        private class Child
        {
            public string FirstName { get; set; }

            public string Gender { get; set; }

            public int Grade { get; set; }

            public Pet[] Pets { get; set; }
        }

        private class Pet
        {
            public string GivenName { get; set; }

            public string Type { get; set; }
        }

        private class Location
        {
            public string State { get; set; }

            public string County { get; set; }

            public string City { get; set; }
        }
    }
}


