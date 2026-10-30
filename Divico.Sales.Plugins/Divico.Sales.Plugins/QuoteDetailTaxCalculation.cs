using System;
using Microsoft.Xrm.Sdk;

namespace Divico.Sales.Plugins
{
    public class QuoteDetailTaxCalculation : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (context.Depth > 1)
                return;

            if (!context.InputParameters.Contains("Target"))
                return;

            var target = context.InputParameters["Target"] as Entity;

            if (target == null || target.LogicalName != "quotedetail")
                return;

            Entity preImage = null;

            if (context.PreEntityImages.Contains("PreImage"))
                preImage = context.PreEntityImages["PreImage"];

            decimal quantity = GetDecimal(target, preImage, "quantity");
            decimal taxPct = GetDecimal(target, preImage, "bsc_taxepercentage");
            decimal price = GetMoney(target, preImage, "priceperunit");

            decimal lineAmount = quantity * price;

            decimal tax = Math.Round(
                lineAmount * (taxPct / 100m),
                2,
                MidpointRounding.AwayFromZero);

            target["tax"] = new Money(tax);
        }

        private decimal GetDecimal(Entity target, Entity preImage, string field)
        {
            if (target.Contains(field))
                return Convert.ToDecimal(target[field]);

            if (preImage != null && preImage.Contains(field))
                return Convert.ToDecimal(preImage[field]);

            return 0m;
        }

        private decimal GetMoney(Entity target, Entity preImage, string field)
        {
            if (target.Contains(field))
                return ((Money)target[field]).Value;

            if (preImage != null && preImage.Contains(field))
                return ((Money)preImage[field]).Value;

            return 0m;
        }
    }
}