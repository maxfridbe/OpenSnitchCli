using Xunit;
using OpenSnitchTUI;

namespace OpenSnitch.Tests
{
    public class TuiEventTests
    {
        [Fact]
        public void TuiEvent_ShouldHaveCommandProperty()
        {
            var evt = new TuiEvent
            {
                Command = "test command"
            };

            Assert.Equal("test command", evt.Command);
        }
        
        [Fact]
        public void TuiEvent_Command_ShouldBeEmptyByDefault()
        {
            var evt = new TuiEvent();
            Assert.Empty(evt.Command);
        }
    }
}
