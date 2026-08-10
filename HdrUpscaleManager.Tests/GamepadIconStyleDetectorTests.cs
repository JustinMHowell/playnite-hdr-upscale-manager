using HdrUpscaleManager.Services;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class GamepadIconStyleDetectorTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Xbox 360 Controller")]
        [InlineData("Xbox Series X Controller")]
        [InlineData("Controller (XBOX 360 For Windows)")]
        [InlineData("Some Unbranded USB Gamepad")]
        public void Detect_NonPlayStationOrUnknownNames_ReturnsXbox(string controllerName)
        {
            Assert.Equal(GamepadIconStyle.Xbox, GamepadIconStyleDetector.Detect(controllerName));
        }

        [Theory]
        [InlineData("Wireless Controller")]
        [InlineData("DualSense Wireless Controller")]
        [InlineData("Sony Interactive Entertainment Wireless Controller")]
        [InlineData("PS4 Controller")]
        [InlineData("PS5 Controller")]
        [InlineData("Some PlayStation-Compatible Pad")]
        public void Detect_PlayStationNames_ReturnsPlayStation(string controllerName)
        {
            Assert.Equal(GamepadIconStyle.PlayStation, GamepadIconStyleDetector.Detect(controllerName));
        }

        [Fact]
        public void Detect_IsCaseInsensitive()
        {
            Assert.Equal(GamepadIconStyle.PlayStation, GamepadIconStyleDetector.Detect("WIRELESS CONTROLLER"));
        }
    }
}
