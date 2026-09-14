using OE.Shared.Extensions;

namespace OE.Shared.Test.Extensions
{
    // IsVirtualSportId answered false for every sport until GM-811, because its list was empty.
    // Six branches in OE.Websocket.Service and one in OE.UOF.Client hang off it, so what sits in
    // that list decides real routing. These tests pin the one id that belongs there and, just as
    // importantly, the ids that must never creep in.
    public class IntExtensionsTests
    {
        private const int VirtualCricket = 11205870;

        [Fact]
        public void IsVirtualSportId_VirtualCricket_IsVirtual()
        {
            Assert.True(VirtualCricket.IsVirtualSportId());
        }

        // 11205863 is 3x3 Basketball, a real sport in the same id block as Virtual Cricket. Nothing
        // about the 112058xx range implies virtual, so this guards against widening by range.
        [Theory]
        [InlineData(10)]        // Soccer
        [InlineData(6)]         // Cricket, the real one
        [InlineData(11205863)]  // 3x3 Basketball
        [InlineData(10915624)]  // eSports
        [InlineData(0)]
        public void IsVirtualSportId_RealSport_IsNotVirtual(int sportId)
        {
            Assert.False(sportId.IsVirtualSportId());
        }

        // The previous provider's virtual sport ids. platform-web's sport-name switch still maps
        // them to virtual_* translation keys, but they are dead in the Genius Sports id space and
        // must not be treated as virtual here.
        [Theory]
        [InlineData(1001)]  // virtual soccer
        [InlineData(1002)]  // virtual basketball
        [InlineData(1003)]  // virtual baseball
        [InlineData(1005)]  // virtual tennis
        [InlineData(1055)]  // virtual horse racing
        [InlineData(1058)]  // virtual dogs
        public void IsVirtualSportId_PreviousProviderVirtualIds_AreNotVirtual(int sportId)
        {
            Assert.False(sportId.IsVirtualSportId());
        }
    }
}
