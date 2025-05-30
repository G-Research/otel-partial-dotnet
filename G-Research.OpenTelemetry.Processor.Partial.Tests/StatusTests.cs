using System.Diagnostics;
using Xunit;

namespace GR.OpenTelemetry.Processor.Partial.Tests
{
    public class StatusTests
    {
        [Fact]
        public void Constructor_ShouldSetCodeAndMessage_ForUnsetStatus()
        {
            var status = new Status(ActivityStatusCode.Unset, "Unset status");

            Assert.Equal(Status.StatusCode.StatusCodeUnset, status.Code);
            Assert.Equal("Unset status", status.Message);
        }

        [Fact]
        public void Constructor_ShouldSetCodeAndMessage_ForOkStatus()
        {
            var status = new Status(ActivityStatusCode.Ok, "Operation successful");

            Assert.Equal(Status.StatusCode.StatusCodeOk, status.Code);
            Assert.Equal("Operation successful", status.Message);
        }

        [Fact]
        public void Constructor_ShouldSetCodeAndMessage_ForErrorStatus()
        {
            var status = new Status(ActivityStatusCode.Error, "An error occurred");

            Assert.Equal(Status.StatusCode.StatusCodeError, status.Code);
            Assert.Equal("An error occurred", status.Message);
        }

        [Fact]
        public void Constructor_ShouldDefaultToUnset_WhenInvalidActivityStatusCodeProvided()
        {
            var status = new Status((ActivityStatusCode)999, "Invalid status");

            Assert.Equal(Status.StatusCode.StatusCodeUnset, status.Code);
            Assert.Equal("Invalid status", status.Message);
        }
    }
}