using Returns.Oms.Api.Client.Models;

namespace GRP_Bot_Agent.Models;

public class LogInData
{
    public string TenantCode { get; set; }

    public string OrderReference { get; set; }

    public string EmailAddress { get; set; }

    public string AuthToken { get; set; }

    public ReturnsOmsApiUseCasesCommonOrderWebResponse Order { get; set; }

    public ReturnsOmsApplicationUseCasesGetCountryConfigurationGetCountryConfigurationResponse BrandCountryConfiguration { get; set; }

    public ReturnsOmsApiUseCasesSearchReturnMethodsSearchReturnMethodsWebResponse AvailableReturnMethods { get; set; }

    public ReturnsOmsApplicationUseCasesSearchReturnMethodsReturnMethodDto SelectedReturnMethod { get; set; }
}