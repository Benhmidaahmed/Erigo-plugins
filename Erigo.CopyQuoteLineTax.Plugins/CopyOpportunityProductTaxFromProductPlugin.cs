using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Erigo.CopyQuoteLineTax.Plugins
{
    public sealed class CopyOpportunityProductTaxFromProductPlugin : IPlugin
    {
        private const string OpportunityProductTable = "opportunityproduct";
        private const string ProductTable = "product";
        private const string PriceLevelTable = "pricelevel";

        private const string ProductLookup = "productid";
        private const string PriceLevelLookup = "pricelevelid";

        private const string SourceTaxField = "bsc_taxepourcentage";
        private const string DestinationTaxField = "bsc_taxepercentage";

        private const string QuantityField = "quantity";
        private const string UnitPriceField = "priceperunit";
        private const string TaxField = "tax";
        private const string PriceOverriddenField = "ispriceoverridden";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracing =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            IPluginExecutionContext context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            if (!string.Equals(
                    context.MessageName,
                    "Create",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    context.PrimaryEntityName,
                    OpportunityProductTable,
                    StringComparison.OrdinalIgnoreCase)
                || context.Stage != 20)
            {
                return;
            }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity))
            {
                return;
            }

            Entity target = (Entity)context.InputParameters["Target"];

            if (target.LogicalName != OpportunityProductTable)
            {
                return;
            }

            try
            {
                EntityReference productReference =
                    target.GetAttributeValue<EntityReference>(ProductLookup);

                if (productReference == null)
                {
                    tracing.Trace(
                        "CopyOpportunityProductTax : aucun produit sélectionné.");
                    return;
                }

                IOrganizationServiceFactory factory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(
                        typeof(IOrganizationServiceFactory));

                IOrganizationService service =
                    factory.CreateOrganizationService(context.UserId);

                Entity product = service.Retrieve(
                    ProductTable,
                    productReference.Id,
                    new ColumnSet(PriceLevelLookup));

                EntityReference priceLevelReference =
                    product.GetAttributeValue<EntityReference>(
                        PriceLevelLookup);

                if (priceLevelReference == null)
                {
                    throw new InvalidPluginExecutionException(
                        "Le produit ne possède pas de liste de prix par défaut.");
                }

                Entity priceLevel = service.Retrieve(
                    PriceLevelTable,
                    priceLevelReference.Id,
                    new ColumnSet(SourceTaxField));

                decimal taxPercentage = GetDecimalValue(
                    priceLevel,
                    SourceTaxField);

                if (taxPercentage < 0m)
                {
                    throw new InvalidPluginExecutionException(
                        "Le pourcentage de taxe ne peut pas être négatif.");
                }

                decimal quantity = GetDecimalValue(
                    target,
                    QuantityField);

                decimal unitPrice = GetMoneyValue(
                    target,
                    UnitPriceField);

                decimal lineAmount = quantity * unitPrice;

                decimal taxAmount = Math.Round(
                    lineAmount * taxPercentage / 100m,
                    2,
                    MidpointRounding.AwayFromZero);

                target[PriceOverriddenField] = true;
                target[DestinationTaxField] = taxPercentage;
                target[TaxField] = new Money(taxAmount);

                tracing.Trace(
                    "CopyOpportunityProductTax : Produit={0}, Taux={1}, " +
                    "Quantité={2}, Prix={3}, Base={4}, Taxe={5}.",
                    productReference.Id,
                    taxPercentage,
                    quantity,
                    unitPrice,
                    lineAmount,
                    taxAmount);
            }
            catch (InvalidPluginExecutionException ex)
            {
                tracing.Trace(
                    "CopyOpportunityProductTax erreur métier : {0}",
                    ex.ToString());

                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    "CopyOpportunityProductTax erreur technique : {0}",
                    ex.ToString());

                throw new InvalidPluginExecutionException(
                    "Impossible de renseigner et calculer la taxe " +
                    "de la ligne de produit d'opportunité.",
                    ex);
            }
        }

        private static decimal GetDecimalValue(
            Entity entity,
            string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.Contains(attributeName) ||
                entity[attributeName] == null)
            {
                return 0m;
            }

            object value = entity[attributeName];

            if (value is decimal)
            {
                return (decimal)value;
            }

            if (value is double)
            {
                return Convert.ToDecimal((double)value);
            }

            if (value is int)
            {
                return (int)value;
            }

            return Convert.ToDecimal(value);
        }

        private static decimal GetMoneyValue(
            Entity entity,
            string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.Contains(attributeName) ||
                entity[attributeName] == null)
            {
                return 0m;
            }

            Money money = entity[attributeName] as Money;

            return money == null ? 0m : money.Value;
        }
    }
}