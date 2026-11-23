using SimInventory.Api.Endpoints;
using SimInventory.Api.Integration;
using SimInventory.Api.Models;
using Xunit;

namespace SimInventory.Api.Tests;

public class IntegrationUnitTests
{
    [Fact]
    public void CsvParserHandlesQuotesCommasAndHeaderCase()
    {
        var table = SimpleCsv.Parse("﻿Employee Number,Full_Name\r\nE1,\"Otieno, Brian \"\"BO\"\"\"\r\nE2,Wanjiru Kamau\r\n");
        Assert.Equal(["employee_number", "full_name"], table.Headers);
        Assert.Equal("Otieno, Brian \"BO\"", table.Rows[0]["full_name"]);
        Assert.Equal(2, table.Rows.Count);
    }

    [Theory]
    [InlineData("2026-10", true, 2026, 10)]
    [InlineData("2026-10-01", true, 2026, 10)]
    [InlineData("10/2026", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    public void PeriodsParseToTheFirstOfTheMonth(string input, bool ok, int year, int month)
    {
        Assert.Equal(ok, CostIntelligenceEndpoints.TryParsePeriod(input, out var period));
        if (ok) Assert.Equal(new DateOnly(year, month, 1), period);
    }

    [Theory]
    [InlineData(1000, 1000, false)]
    [InlineData(1499, 1000, false)]
    [InlineData(1500, 1000, true)]
    [InlineData(500, 1000, true)]
    [InlineData(100, 0, false)]
    public void RuleFlagsBillsFarFromTheContract(decimal amount, decimal contracted, bool flagged) =>
        Assert.Equal(flagged, CostIntelligenceEndpoints.EvaluateRule(amount, contracted).Flagged);

    [Fact]
    public void WebhookSignatureRejectsTamperingAndReplay()
    {
        const string secret = "0123456789abcdef0123456789abcdef";
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds().ToString();
        var signature = HrWebhookSignature.Sign(secret, ts, "{\"a\":1}");
        Assert.True(HrWebhookSignature.IsValid(secret, ts, signature, "{\"a\":1}", now));
        Assert.False(HrWebhookSignature.IsValid(secret, ts, signature, "{\"a\":2}", now));
        Assert.False(HrWebhookSignature.IsValid(secret, ts, signature, "{\"a\":1}", now.AddMinutes(10)));
        Assert.False(HrWebhookSignature.IsValid("another-secret-0123456789abcdefgh", ts, signature, "{\"a\":1}", now));
    }

    [Theory]
    [InlineData("Active", EmploymentStatus.Active)]
    [InlineData("on leave", EmploymentStatus.Inactive)]
    [InlineData("RESIGNED", EmploymentStatus.Terminated)]
    public void HrStatusesMapToEmploymentStatus(string value, EmploymentStatus expected)
    {
        Assert.True(EmployeeDirectorySync.TryParseStatus(value, out var status));
        Assert.Equal(expected, status);
    }

    [Fact]
    public void UnknownHrStatusIsRejectedRatherThanGuessed() =>
        Assert.False(EmployeeDirectorySync.TryParseStatus("maybe", out _));

    [Fact]
    public void MigrationScriptsAreEmbeddedInOrder()
    {
        var ids = Data.SchemaMigrator.LoadScripts().Select(s => s.Id).ToList();
        Assert.Equal(["0001_baseline", "0002_governance_billing_ml_hr", "0003_audit_append_only"], ids);
    }
}
