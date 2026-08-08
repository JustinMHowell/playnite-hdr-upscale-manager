using Xunit;

namespace HdrUpscaleManager.UiTests
{
    /* Every test class in this project kills/launches a single real Playnite.DesktopApp
       process and patches the same global config.json (see PlayniteFixture) — xUnit runs
       different test classes in parallel by default, which would have two of these fight
       over both. Putting every one of them in this collection (DisableParallelization=true)
       forces them to run one at a time instead, which is what "tests should be isolated"
       actually requires here — isolated meaning "don't stomp on each other," not "don't
       share a collection." */
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class PlayniteUiCollection
    {
        public const string Name = "Playnite UI";
    }
}
