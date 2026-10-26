using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class InsuranceTextTests : IDisposable
{
    public InsuranceTextTests() => Loc.English = false;
    public void Dispose() => Loc.English = false;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Results_and_the_insurance_header_show_the_same_server_confirmed_notice(bool english)
    {
        Loc.English = english;
        var quote = new EnhanceQuote { InsuranceLapsed = true };
        var receipt = new EnhanceReceipt { InsuranceLapsed = true };
        var expected = english ? "Insurance has lapsed. Please insure again." : "보험이 풀렸습니다. 다시 가입하세요";
        Assert.Equal(expected, InsuranceText.Notice(quote));
        Assert.Equal(expected, InsuranceText.Notice(receipt));
        // The receipt remains truthful even if a plate payment or a failed roll caused the lapse.
        receipt.Action = EnhanceAction.RegisterInserts;
        Assert.Equal(expected, InsuranceText.Notice(receipt));
        quote.CurrentInsurance = InsuranceTier.Premium;
        Assert.Equal("", InsuranceText.Notice(quote));
        Assert.Null(InsuranceText.Notice(new EnhanceReceipt { Success = true }));
        Assert.Equal("", InsuranceText.Notice(new EnhanceQuote()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_card_names_the_payment_credit_and_omits_it_for_old_quotes(bool english)
    {
        Loc.English = english;
        var quote = new EnhanceQuote { PreviousInsurancePaid = 12345 };
        var expected = english ? $"Previous payment {12345:N0} ₽ credited" : $"이전 납부 {12345:N0} ₽ 차감";
        Assert.Equal(expected, InsuranceText.Credit(quote));
        Assert.Equal("", InsuranceText.Credit(new EnhanceQuote()));
        Assert.Equal("", InsuranceText.Credit(null!));
    }
}
