using RagServer.Infrastructure.Catalog;
using Xunit;

namespace RagServer.Tests.Infrastructure;

public class BusinessRulesRepositoryTests
{
    [Fact]
    public async Task Given_NullRepository_When_GetRules_Then_ReturnsEmpty()
    {
        // Arrange
        var repo = new NullBusinessRulesRepository();

        // Act
        var rules = await repo.GetRulesAsync("Counterparty");

        // Assert
        Assert.Empty(rules);
    }

    [Fact]
    public async Task Given_NullRepository_When_GetMandatoryRules_Then_ReturnsEmpty()
    {
        // Arrange
        var repo = new NullBusinessRulesRepository();

        // Act
        var rules = await repo.GetRulesAsync("Book", mandatoryOnly: true);

        // Assert
        Assert.Empty(rules);
    }
}
