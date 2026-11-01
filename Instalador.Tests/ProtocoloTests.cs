using Xunit;

namespace PbsuLoader.Testes
{
    public class ProtocoloTests
    {
        [Theory]
        [InlineData("pbsloader://mod/MeuMod", "MeuMod")]
        [InlineData("pbsloader://mod/freewalk-caminhar-a-pe/", "freewalk-caminhar-a-pe")]
        [InlineData("PBSLOADER://MOD/Outro", "Outro")]
        [InlineData("pbsloader://Outro", "Outro")]
        [InlineData("pbsloader://mod/Um%20Nome", null)]
        [InlineData("pbsloader://mod/", null)]
        [InlineData("https://exemplo.com/mod/Um", null)]
        [InlineData(null, null)]
        public void IdDoArgumento_Parseia(string arg, string esperado)
            => Assert.Equal(esperado, Protocolo.IdDoArgumento(arg));
    }
}
