using System;
using System.Linq;
using BlockNations.Simulation;
using NUnit.Framework;

public sealed class AdvancedEconomyTests
{
    private static void Cycle(AdvancedMatch match)
    {
        Assert.That(match.Apply(new MatchCommand(MatchActionKind.EndTurn, match.Board.CurrentTurnSeat)).Applied, Is.True);
        Assert.That(match.Apply(new MatchCommand(MatchActionKind.EndTurn, match.Board.CurrentTurnSeat)).Applied, Is.True);
    }
    private static int Recruit(AdvancedMatch match, PopulationSex sex = PopulationSex.Male, string type = UnitRegistry.WarriorTypeId)
    {
        int seat = match.Board.CurrentTurnSeat;
        int city = match.Board.Cities.Single(c => c.Seat == seat).Id;
        Assert.That(match.TryRecruit(seat, city, type, sex, out string reason), Is.True, reason);
        return match.Board.UnitAt(match.Board.GetCity(city).Position).Id;
    }

    [TestCase(0)] [TestCase(1)]
    public void OpeningIsSevenBySevenHumanMatchWithNoStandardIncomeAndExplicitStarter(int starter)
    {
        var match = new AdvancedMatch(firstSeat: starter);
        Assert.That(match.Board.Width, Is.EqualTo(7)); Assert.That(match.Board.Height, Is.EqualTo(7));
        Assert.That(match.Board.CurrentTurnSeat, Is.EqualTo(starter));
        Assert.That(match.Board.IncomePerCity, Is.Zero); Assert.That(match.Board.Units, Is.Empty);
        Assert.That(match.Board.GoldForSeat(starter), Is.EqualTo(9));
        Assert.That(match.Board.GoldForSeat(1 - starter), Is.EqualTo(7));
        match.Apply(new MatchCommand(MatchActionKind.EndTurn, starter));
        Assert.That(match.Board.GoldForSeat(1 - starter), Is.EqualTo(10));
        Assert.That(match.CityEconomy(starter + 1).GrowthProgress, Is.EqualTo(6));
    }

    [TestCase(PopulationSex.Male)] [TestCase(PopulationSex.Female)]
    public void RecruitingTransfersChosenSexRatherThanCreatingOrDestroyingResidents(PopulationSex sex)
    {
        var match = new AdvancedMatch(); int gold = match.Board.GoldForSeat(0);
        int id = Recruit(match, sex); var city = match.CityEconomy(1);
        Assert.That(city.Available(sex), Is.EqualTo(5)); Assert.That(city.Civilians, Is.EqualTo(11));
        Assert.That(match.Soldier(id).Sex, Is.EqualTo(sex)); Assert.That(match.Soldier(id).HomeCityId, Is.EqualTo(1));
        var forecast = match.Forecast(1);
        Assert.That(forecast.Soldiers, Is.EqualTo(1)); Assert.That(forecast.Residents, Is.EqualTo(12));
        Assert.That(forecast.FoodConsumed, Is.EqualTo(12)); Assert.That(forecast.Pairs, Is.EqualTo(5));
        Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(gold - 2));
        Assert.That(match.GoldUpkeep(0), Is.EqualTo(1));
    }

    [TestCase(1, 1, PopulationSex.Male)] [TestCase(0, 2, PopulationSex.Male)]
    [TestCase(0, 1, (PopulationSex)99)]
    public void InvalidRecruitLeavesGoldPopulationAndBoardUntouched(int seat, int city, PopulationSex sex)
    {
        var match = new AdvancedMatch();
        Assert.That(match.TryRecruit(seat, city, UnitRegistry.WarriorTypeId, sex, out _), Is.False);
        Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(9));
        Assert.That(match.CityEconomy(1).Civilians, Is.EqualTo(12)); Assert.That(match.Board.Units, Is.Empty);
    }

    [Test]
    public void RawStandardRecruitCommandCannotBypassPopulationSelection()
    {
        var match = new AdvancedMatch();
        Assert.That(match.Apply(new MatchCommand(MatchActionKind.Recruit, 0, 1, match.Board.GetCity(1).Position,
            recruitType: UnitRegistry.WarriorTypeId)).Applied, Is.False);
        Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(9)); Assert.That(match.Board.Units, Is.Empty);
    }

    [Test]
    public void OccupiedCityAndInsufficientGoldCannotConsumePopulation()
    {
        var match = new AdvancedMatch(); Recruit(match);
        Assert.That(match.TryRecruit(0, 1, UnitRegistry.WarriorTypeId, PopulationSex.Female, out _), Is.False);
        Assert.That(match.CityEconomy(1).Women, Is.EqualTo(6));
        var poor = new AdvancedMatch(); poor.Board.SetGold(0, 1);
        Assert.That(poor.TryRecruit(0, 1, UnitRegistry.WarriorTypeId, PopulationSex.Female, out _), Is.False);
        Assert.That(poor.CityEconomy(1).Women, Is.EqualTo(6));
    }

    [Test]
    public void RecruitmentForecastMatchesActualNextStateAndDoesNotMutateIt()
    {
        var match = new AdvancedMatch(); var preview = match.Forecast(1, PopulationSex.Female);
        Assert.That(match.CityEconomy(1).Women, Is.EqualTo(6)); Recruit(match, PopulationSex.Female);
        var actual = match.Forecast(1);
        Assert.That(actual.Civilians, Is.EqualTo(preview.Civilians)); Assert.That(actual.Pairs, Is.EqualTo(preview.Pairs));
        Assert.That(actual.Residents, Is.EqualTo(preview.Residents)); Assert.That(actual.FoodProduced, Is.EqualTo(preview.FoodProduced));
        Assert.That(actual.GoldProduced, Is.EqualTo(preview.GoldProduced));
    }

    [Test]
    public void PairingBirthsConsumeFoodAndAlternateSexWithoutRandomness()
    {
        var match = new AdvancedMatch(); Cycle(match);
        Assert.That(match.CityEconomy(1).Men, Is.EqualTo(7)); Assert.That(match.CityEconomy(1).Women, Is.EqualTo(6));
        Assert.That(match.CityEconomy(1).StoredFood, Is.EqualTo(18)); Assert.That(match.LastTurnReport.Births, Is.EqualTo(1));
        Cycle(match); Cycle(match);
        Assert.That(match.CityEconomy(1).Women, Is.EqualTo(7));
    }

    [Test]
    public void LosingMenReducesGrowthAndMissingEitherSexStopsBirths()
    {
        var match = new AdvancedMatch(); var city = match.CityEconomy(1);
        city.Men = 1; city.GrowthProgress = 0; Cycle(match);
        Assert.That(city.GrowthProgress, Is.EqualTo(1));
        city.Men = 0; Cycle(match); Cycle(match);
        Assert.That(city.GrowthProgress, Is.EqualTo(1)); Assert.That(city.Women, Is.EqualTo(6));
    }

    [Test]
    public void HousingCountsSoldiersAndDoesNotBankExtraBirthProgressWhenFull()
    {
        var match = new AdvancedMatch(); Recruit(match);
        var city = match.CityEconomy(1); city.Men = 7; city.Women = 8; city.FoodLevel = 1; city.GrowthProgress = 11;
        Assert.That(match.Forecast(1).Residents, Is.EqualTo(16)); Cycle(match);
        Assert.That(city.Civilians, Is.EqualTo(15)); Assert.That(city.GrowthProgress, Is.EqualTo(11));
        Assert.That(match.LastTurnReport.Births, Is.Zero);
    }

    [Test]
    public void NoFoodSurplusPausesGrowthEvenWithStoredFood()
    {
        var match = new AdvancedMatch(); var city = match.CityEconomy(1);
        city.Men = 8; city.Women = 8; city.HousingLevel = 1; city.GrowthProgress = 11;
        Cycle(match); Assert.That(city.GrowthProgress, Is.EqualTo(11)); Assert.That(city.Civilians, Is.EqualTo(16));
    }

    [Test]
    public void FoodReserveBuffersShortageThenStarvationHasBoundedCivilianLoss()
    {
        var match = new AdvancedMatch(); var city = match.CityEconomy(1);
        city.Men = 10; city.Women = 10; city.StoredFood = 4; city.GrowthProgress = 9;
        Cycle(match); Assert.That(city.Civilians, Is.EqualTo(20)); Assert.That(city.StoredFood, Is.Zero);
        Cycle(match); Assert.That(city.Civilians, Is.EqualTo(19)); Assert.That(city.GrowthProgress, Is.Zero);
        Assert.That(match.LastTurnReport.FoodShortfall, Is.EqualTo(4));
        Assert.That(match.LastTurnReport.CivilianDeaths, Is.EqualTo(1));
    }

    [Test]
    public void SoldiersConsumeFoodAndCannotSurviveWithoutCiviliansOrRationsForever()
    {
        var match = new AdvancedMatch(); int id = Recruit(match); var city = match.CityEconomy(1);
        city.Men = 0; city.Women = 0; city.StoredFood = 0;
        Cycle(match);
        Assert.That(match.LastTurnReport.UnfedUnits, Is.EqualTo(1));
        Assert.That(match.Board.GetUnit(id).Health, Is.EqualTo(20));
        Cycle(match); Cycle(match);
        Assert.That(match.Board.GetUnit(id), Is.Null); Assert.That(match.Soldier(id), Is.Null);
    }

    [TestCase(CityUpgrade.Housing)] [TestCase(CityUpgrade.Food)] [TestCase(CityUpgrade.Gold)]
    public void UpgradesChargeGoldAndResetOnePurchaseLimitOnOwnerTurn(CityUpgrade upgrade)
    {
        var match = new AdvancedMatch();
        Assert.That(match.TryUpgrade(0, 1, upgrade, out _), Is.True);
        Assert.That(match.CityEconomy(1).Level(upgrade), Is.EqualTo(1)); Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(5));
        Assert.That(match.TryUpgrade(0, 1, upgrade, out _), Is.False);
        Cycle(match); Assert.That(match.CityEconomy(1).UpgradedThisTurn, Is.False);
        Assert.That(match.TryUpgrade(0, 1, upgrade, out _), Is.True);
        Assert.That(match.Board.GoldForSeat(0), Is.Zero);
    }

    [Test]
    public void UpgradeRejectsEnemyWrongTurnUnknownAndMaximumLevelWithoutCharging()
    {
        var match = new AdvancedMatch();
        Assert.That(match.TryUpgrade(1, 2, CityUpgrade.Food, out _), Is.False);
        Assert.That(match.TryUpgrade(0, 2, CityUpgrade.Food, out _), Is.False);
        Assert.That(match.TryUpgrade(0, 1, (CityUpgrade)99, out _), Is.False);
        match.CityEconomy(1).FoodLevel = 3;
        Assert.That(match.TryUpgrade(0, 1, CityUpgrade.Food, out _), Is.False);
        Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(9));
    }

    [Test]
    public void MobilizationCutsWorkerOutputRatherThanCreatingFreeFoodAndGold()
    {
        var match = new AdvancedMatch(); var city = match.CityEconomy(1); city.Men = 2; city.Women = 2;
        city.FoodLevel = 3; city.GoldLevel = 3;
        Assert.That(match.Forecast(1).FoodProduced, Is.EqualTo(8)); Assert.That(match.Forecast(1).GoldProduced, Is.EqualTo(1));
    }

    [Test]
    public void UpkeepUsesIncomeAndStockpileAndUnpaidUnitsLoseHealthUntilRemoved()
    {
        var match = new AdvancedMatch(); int id = Recruit(match);
        var city = match.CityEconomy(1); city.Men = 0; city.Women = 0; city.StoredFood = 24;
        match.Board.SetGold(0, 0); Cycle(match);
        Assert.That(match.LastTurnReport.UnpaidUnits, Is.EqualTo(1));
        Assert.That(match.Board.GetUnit(id).Health, Is.EqualTo(20)); Assert.That(match.Board.GoldForSeat(0), Is.Zero);
        Cycle(match); Cycle(match);
        Assert.That(match.Board.GetUnit(id), Is.Null); Assert.That(match.GoldUpkeep(0), Is.Zero);
        Assert.That(match.Soldiers, Is.Empty);
    }

    [Test]
    public void DisbandReturnsCorrectSexAndStopsSameTurnRecruitmentLoop()
    {
        var match = new AdvancedMatch(); int id = Recruit(match, PopulationSex.Female);
        int gold = match.Board.GoldForSeat(0);
        Assert.That(match.TryDisband(1, id, out _), Is.False);
        Assert.That(match.TryDisband(0, id, out _), Is.True);
        Assert.That(match.CityEconomy(1).Women, Is.EqualTo(6)); Assert.That(match.GoldUpkeep(0), Is.Zero);
        Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(gold));
        Assert.That(match.TryRecruit(0, 1, UnitRegistry.WarriorTypeId, PopulationSex.Female, out _), Is.False);
    }

    [Test]
    public void DisbandRequiresHomeCityAndUnusedActions()
    {
        var match = new AdvancedMatch(); int id = Recruit(match);
        var move = new MatchCommand(MatchActionKind.Move, 0, id, match.Board.Position(2, 1));
        Assert.That(match.Apply(move).Applied, Is.True);
        Assert.That(match.TryDisband(0, id, out _), Is.False);
        Cycle(match);
        Assert.That(match.Apply(new MatchCommand(MatchActionKind.Move, 0, id, match.Board.Position(1, 1))).Applied, Is.True);
        Assert.That(match.TryDisband(0, id, out _), Is.False);
        Cycle(match); Assert.That(match.TryDisband(0, id, out _), Is.True);
    }

    [Test]
    public void CombatDeathsDoNotReturnLostPopulationAndStopFutureUpkeep()
    {
        var match = new AdvancedMatch(); int defenderId = Recruit(match); var defender = match.Board.GetUnit(defenderId);
        match.Apply(new MatchCommand(MatchActionKind.EndTurn, 0));
        var killer = new UnitDefinition("fixture", "Fixture", 0, 2, "fixture", 30, 30, 2, true, 0, 1, 1);
        match.Board.AddUnit(new SimulationUnit(100, 1, match.Board.Position(2, 1), killer));
        var result = match.Apply(new MatchCommand(MatchActionKind.Attack, 1, 100, defender.Position, defenderId));
        Assert.That(result.KilledUnitId, Is.EqualTo(defenderId)); Assert.That(match.Soldier(defenderId), Is.Null);
        Assert.That(match.CityEconomy(1).Men, Is.EqualTo(5)); Assert.That(match.GoldUpkeep(0), Is.Zero);
    }

    [TestCase(RecruitmentBalance.Equal, "warrior", 10)]
    [TestCase(RecruitmentBalance.ClassStrengthExperiment, "warrior", 8)]
    [TestCase(RecruitmentBalance.ClassStrengthExperiment, "archer", 9)]
    [TestCase(RecruitmentBalance.ClassStrengthExperiment, "rider", 9)]
    public void CombatPresetIsClassSpecificAndDoesNotMutateOfficialUnitDefinitions(RecruitmentBalance balance, string type, int attack)
    {
        var match = new AdvancedMatch(balance); int id = Recruit(match, PopulationSex.Female, type);
        Assert.That(match.Board.GetUnit(id).Definition.AttackUnits, Is.EqualTo(attack));
        Assert.That(UnitRegistry.GetDefinitionOrDefault(type).AttackUnits, Is.EqualTo(10));
    }

    [Test]
    public void TwoIdenticalHumanActionSequencesProduceIdenticalEconomyWithoutRandomness()
    {
        var a = new AdvancedMatch(); var b = new AdvancedMatch();
        foreach (var match in new[] { a, b })
        {
            Recruit(match, PopulationSex.Female);
            match.TryUpgrade(0, 1, CityUpgrade.Food, out _);
            Cycle(match); Cycle(match); Cycle(match);
        }
        foreach (int id in new[] { 1, 2 })
        {
            Assert.That(a.CityEconomy(id).Men, Is.EqualTo(b.CityEconomy(id).Men));
            Assert.That(a.CityEconomy(id).Women, Is.EqualTo(b.CityEconomy(id).Women));
            Assert.That(a.CityEconomy(id).StoredFood, Is.EqualTo(b.CityEconomy(id).StoredFood));
            Assert.That(a.Board.GoldForSeat(id - 1), Is.EqualTo(b.Board.GoldForSeat(id - 1)));
        }
    }

    [Test]
    public void StandardSimulationRecruitmentAndIncomeRemainGoldOnly()
    {
        var board = new MatchState(7, 7, 2, UnitRegistry.AllDefinitions);
        board.AddCity(new SimulationCity(1, 0, board.Position(1, 1))); board.SetGold(0, 2);
        Assert.That(MatchEngine.Apply(board, new MatchCommand(MatchActionKind.Recruit, 0, 1, board.Position(1, 1), recruitType: "warrior")).Applied, Is.True);
        MatchEngine.BeginTurn(board, 0); Assert.That(board.GoldForSeat(0), Is.EqualTo(1));
    }

    [Test]
    public void WrongSeatEndTurnCannotSettleEconomyTwice()
    {
        var match = new AdvancedMatch(); var report = match.LastTurnReport;
        Assert.That(match.Apply(new MatchCommand(MatchActionKind.EndTurn, 1)).Applied, Is.False);
        Assert.That(match.Board.CurrentTurnSeat, Is.Zero); Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(9));
        Assert.That(match.CityEconomy(1).GrowthProgress, Is.EqualTo(6));
        Assert.That(match.LastTurnReport, Is.SameAs(report));
    }

    [Test]
    public void RecruitingLastManIsAllowedButForecastClearlyStopsPairing()
    {
        var match = new AdvancedMatch(); match.CityEconomy(1).Men = 1;
        Assert.That(match.Forecast(1, PopulationSex.Male).Pairs, Is.Zero);
        Recruit(match); Assert.That(match.CityEconomy(1).Men, Is.Zero);
        Assert.That(match.TryRecruit(0, 1, UnitRegistry.WarriorTypeId, PopulationSex.Male, out _), Is.False);
        match.CityEconomy(1).GrowthProgress = 0; Cycle(match);
        Assert.That(match.CityEconomy(1).GrowthProgress, Is.Zero);
    }

    [Test]
    public void StoredFoodCannotGrowPastItsCap()
    {
        var match = new AdvancedMatch(); var city = match.CityEconomy(1);
        city.StoredFood = 24; city.HousingLevel = 3; city.FoodLevel = 3; city.GrowthProgress = 0;
        Cycle(match); Assert.That(city.StoredFood, Is.EqualTo(24));
    }

    [Test]
    public void CaptureWinsAndStopsFurtherPurchasesAndTurnSettlement()
    {
        var match = new AdvancedMatch();
        match.Board.AddUnit(new SimulationUnit(100, 0, match.Board.Position(4, 5), UnitRegistry.Warrior));
        var result = match.Apply(new MatchCommand(MatchActionKind.Move, 0, 100, match.Board.Position(5, 5)));
        Assert.That(result.CapturedCityId, Is.EqualTo(2)); Assert.That(match.Board.WinnerSeat, Is.Zero);
        var report = match.LastTurnReport;
        Assert.That(match.TryRecruit(0, 1, UnitRegistry.WarriorTypeId, PopulationSex.Male, out _), Is.False);
        Assert.That(match.TryUpgrade(0, 1, CityUpgrade.Housing, out _), Is.False);
        Assert.That(match.Apply(new MatchCommand(MatchActionKind.EndTurn, 0)).Applied, Is.False);
        Assert.That(match.LastTurnReport, Is.SameAs(report));
    }

    [Test]
    public void UpgradeForecastShowsCurrentWorkerLimitWithoutMutatingCity()
    {
        var match = new AdvancedMatch();
        Assert.That(match.Forecast(1, upgrade: CityUpgrade.Food).FoodProduced, Is.EqualTo(20));
        Assert.That(match.Forecast(1, upgrade: CityUpgrade.Gold).GoldProduced, Is.EqualTo(3),
            "A market raises capacity but needs more workers to produce extra gold.");
        Assert.That(match.Forecast(1, upgrade: CityUpgrade.Housing).Housing, Is.EqualTo(20));
        Assert.That(match.CityEconomy(1).FoodLevel, Is.Zero); Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(9));
    }
}
