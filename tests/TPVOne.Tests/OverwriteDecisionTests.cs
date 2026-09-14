using TPVOne.LegacyAccess.Core.Import;

namespace TPVOne.Tests;

public sealed class OverwriteDecisionTests
{
    [Theory]
    [InlineData('S', true)]
    [InlineData('s', true)]
    [InlineData('N', false)]
    [InlineData('n', false)]
    public void Parse_AcceptsYesAndNo(char key, bool expected)
    {
        Assert.Equal(expected, OverwriteDecision.Parse(key));
    }

    [Theory]
    [InlineData('X')]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData(' ')]
    [InlineData('1')]
    public void Parse_IgnoresOtherKeys(char key)
    {
        Assert.Null(OverwriteDecision.Parse(key));
    }

    [Fact]
    public void FromCharacters_UsesFirstValidKeyAfterIgnoredOnes()
    {
        Assert.True(OverwriteDecision.FromCharacters(['x', '\r', 'S']));
        Assert.False(OverwriteDecision.FromCharacters(['1', 'n']));
        Assert.True(OverwriteDecision.FromCharacters(['s']));
        Assert.False(OverwriteDecision.FromCharacters(['N']));
    }

    [Fact]
    public void Protocol_ParsesOverwriteRequest()
    {
        Assert.True(OverwriteConfirmationProtocol.TryParseRequest(
            "TPVONE_OVERWRITE?:alergenos",
            out var tableName));
        Assert.Equal("alergenos", tableName);
        Assert.Equal(
            "La tabla 'alergenos' ya existe. ¿Sobrescribir? [S/N]: ",
            OverwriteConfirmationProtocol.Prompt("alergenos"));
    }
}
