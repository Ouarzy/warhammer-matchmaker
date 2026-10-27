using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Domain.Tests.ArmyLists;

public class WeaponTests
{
    private static Weapon WithAbilities(string abilities) => new("Boltgun", "24\"", "2", "2+", "4", "0", "1", abilities);

    [Theory]
    [InlineData("Assault, Close-quarters\nClose-quarters: ", "Assault, Close-quarters")]
    [InlineData("Anti-PSYKER 4+, Assault, Rapid Fire 1\nAnti-PSYKER 4+: ", "Anti-PSYKER 4+, Assault, Rapid Fire 1")]
    [InlineData("Cleave 1, Sustained Hits 1: non-MONSTER/VEHICLE\nSustained Hits 1: non-MONSTER/VEHICLE: ", "Cleave 1, Sustained Hits 1: non-MONSTER/VEHICLE")]
    [InlineData("Assault, Rapid Fire 2", "Assault, Rapid Fire 2")]
    [InlineData("-", "-")]
    public void Rule_reminders_without_description_are_not_repeated(string abilities, string expected) =>
        Assert.Equal(expected, WithAbilities(abilities).DisplayedAbilities);

    [Fact]
    public void Rule_reminders_with_a_description_are_kept()
    {
        var weapon = WithAbilities("Assault, Psychic Shock\nPsychic Shock: Each time this weapon hits, roll a D6.");

        Assert.Equal("Assault, Psychic Shock · Psychic Shock: Each time this weapon hits, roll a D6.", weapon.DisplayedAbilities);
    }
}
