using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Divico.Sales.Plugins
{
    public sealed class CopyQuoteLineTaxFromProductPlugin : IPlugin
    {
        private const string QuoteDetailTable = "quotedetail";
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
            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            if (!string.Equals(
                    context.MessageName,
                    "Create",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    context.PrimaryEntityName,
                    QuoteDetailTable,
                    StringComparison.OrdinalIgnoreCase)
                || context.Stage != 20)
            {
                return;
            }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != QuoteDetailTable)
            {
                return;
            }

            try
            {
                var productReference =
                    target.GetAttributeValue<EntityReference>(ProductLookup);

                if (productReference == null)
                {
                    tracing.Trace(
                        "CopyQuoteLineTax: pas de productid, aucune taxe calculée.");
                    return;
                }

                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(
                    typeof(IOrganizationServiceFactory));

                var service = factory.CreateOrganizationService(context.UserId);

                var product = service.Retrieve(
                    ProductTable,
                    productReference.Id,
                    new ColumnSet(PriceLevelLookup));

                var priceLevelReference =
                    product.GetAttributeValue<EntityReference>(PriceLevelLookup);

                if (priceLevelReference == null)
                {
                    throw new InvalidPluginExecutionException(
                        "Le produit ne possède pas de liste de prix par défaut.");
                }

                var priceLevel = service.Retrieve(
                    PriceLevelTable,
                    priceLevelReference.Id,
                    new ColumnSet(SourceTaxField));

                var taxPercentage = GetDecimalValue(
                    priceLevel,
                    SourceTaxField);

                if (taxPercentage < 0m)
                {
                    throw new InvalidPluginExecutionException(
                        "Le pourcentage de taxe ne peut pas être négatif.");
                }

                var quantity = GetDecimalValue(target, QuantityField);
                var unitPrice = GetMoneyValue(target, UnitPriceField);

                var lineAmount = quantity * unitPrice;

                var taxAmount = Math.Round(
                    lineAmount * taxPercentage / 100m,
                    2,
                    MidpointRounding.AwayFromZero);

                target[PriceOverriddenField] = true;
                target[DestinationTaxField] = taxPercentage;
                target[TaxField] = new Money(taxAmount);

                tracing.Trace(
                    "CopyQuoteLineTax: Produit={0}, Taux={1}, Quantité={2}, " +
                    "Prix={3}, Base={4}, Taxe={5}.",
                    productReference.Id,
                    taxPercentage,
                    quantity,
                    unitPrice,
                    lineAmount,
                    taxAmount);
            }
            catch (InvalidPluginExecutionException ex)
            {
                tracing.Trace("CopyQuoteLineTax erreur métier : {0}", ex);
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace("CopyQuoteLineTax erreur technique : {0}", ex);

                throw new InvalidPluginExecutionException(
                    "Impossible de renseigner et calculer la taxe de la ligne.",
                    ex);
            }
        }

        private static decimal GetDecimalValue(Entity entity, string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.TryGetValue(attributeName, out var value) ||
                value == null)
            {
                return 0m;
            }

            if (value is decimal decimalValue)
                return decimalValue;

            if (value is double doubleValue)
                return Convert.ToDecimal(doubleValue);

            if (value is int intValue)
                return intValue;

            return Convert.ToDecimal(value);
        }

        private static decimal GetMoneyValue(Entity entity, string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.TryGetValue(attributeName, out var value) ||
                value == null)
            {
                return 0m;
            }

            return value is Money money ? money.Value : 0m;
        }
    }
}