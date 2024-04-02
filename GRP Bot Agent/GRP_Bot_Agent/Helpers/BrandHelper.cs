using System.Collections.Generic;
using FuzzySharp;

namespace GRP_Bot_Agent.Helpers;

public static class BrandHelper
{
    private static readonly Dictionary<string, string> RetailerToTenantCodeMap = new()
    {
        { "Nike", "NKENKE" }
    };

    public static string GetTenantCode(string retailerNameInput)
    {
        var matchedRetailer = Process.ExtractOne(retailerNameInput, RetailerToTenantCodeMap.Keys, cutoff: 60);

        if (matchedRetailer is null || !RetailerToTenantCodeMap.TryGetValue(matchedRetailer.Value, out var tenantCode))
        {
            tenantCode = "FBAFBA";
        }

        return tenantCode;
    }
}