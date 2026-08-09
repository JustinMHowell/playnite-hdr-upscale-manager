using System.Collections.Generic;
using HdrUpscaleManager.Models.Addons;
using Newtonsoft.Json;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class LumaPrerequisitesManifestTests
    {
        [Fact]
        public void GetStepsFor_GameListed_ReturnsItsSteps()
        {
            var manifest = new LumaPrerequisitesManifest
            {
                Prerequisites = new Dictionary<string, List<LumaPrerequisiteStep>>
                {
                    ["Vanquish"] = new List<LumaPrerequisiteStep>
                    {
                        new LumaPrerequisiteStep { Type = "dgvoodoo2", Reason = "needs a DX9->11 wrapper" }
                    }
                }
            };

            var steps = manifest.GetStepsFor("Vanquish");

            Assert.Single(steps);
            Assert.Equal("dgvoodoo2", steps[0].Type);
        }

        [Fact]
        public void GetStepsFor_GameNotListed_ReturnsEmpty()
        {
            var manifest = new LumaPrerequisitesManifest();

            var steps = manifest.GetStepsFor("Some Other Game");

            Assert.Empty(steps);
        }

        [Fact]
        public void GetStepsFor_NullGameName_ReturnsEmpty()
        {
            var manifest = new LumaPrerequisitesManifest();

            var steps = manifest.GetStepsFor(null);

            Assert.Empty(steps);
        }

        [Fact]
        public void Normalize_MakesLookupCaseInsensitive()
        {
            // Simulates what a real JSON round-trip does: Json.NET replaces the dictionary with
            // a default-comparer instance during deserialization, same gap RemoteManifest.Normalize
            // exists to close for its own dictionaries.
            var json = @"{ ""version"": 1, ""prerequisites"": { ""Vanquish"": [ { ""type"": ""dgvoodoo2"", ""reason"": ""x"" } ] } }";
            var manifest = JsonConvert.DeserializeObject<LumaPrerequisitesManifest>(json);
            LumaPrerequisitesManifest.Normalize(manifest);

            var steps = manifest.GetStepsFor("vanquish"); // different casing than the JSON key

            Assert.Single(steps);
        }

        [Fact]
        public void Deserialize_RealManifestShape_ParsesCorrectly()
        {
            // Matches the actual committed luma_prerequisites.json / embedded fallback copy.
            const string json = @"
                {
                  ""version"": 1,
                  ""prerequisites"": {
                    ""Vanquish"": [
                      {
                        ""type"": ""dgvoodoo2"",
                        ""reason"": ""Vanquish never shipped a DirectX 11 renderer, but Luma's shader hooks only target DX11.""
                      }
                    ]
                  }
                }";

            var manifest = JsonConvert.DeserializeObject<LumaPrerequisitesManifest>(json);
            LumaPrerequisitesManifest.Normalize(manifest);

            Assert.Equal(1, manifest.Version);
            var steps = manifest.GetStepsFor("Vanquish");
            Assert.Single(steps);
            Assert.Equal("dgvoodoo2", steps[0].Type);
            Assert.Contains("DirectX 11", steps[0].Reason);
        }
    }
}
