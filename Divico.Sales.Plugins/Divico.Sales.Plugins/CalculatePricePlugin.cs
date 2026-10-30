using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Divico.Sales.Plugins
{
    public class CalculatePricePlugin : IPlugin
    {
        private const string EntityQuote = "quote";
        private const string EntityQuoteDetail = "quotedetail";

        private const string QuoteDiscountPercentage = "discountpercentage";
        private const string QuoteDiscountAmount = "discountamount";
        private const string QuoteNetHt = "bsc_netapresremise";
        private const string QuoteCustomTax = "bsc_customtax";
        private const string QuoteCustomTotal = "bsc_customtotal";

        private const string QuoteDetailQuote = "quoteid";
        private const string QuoteDetailBaseAmount = "baseamount";
        private const string QuoteDetailTaxRate = "bsc_taxepercentage";

        private const string PreImageName = "PreImage";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(
                typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                tracing.Trace(
                    $"CalculatePricePlugin: début. " +
                    $"Entité={context.PrimaryEntityName}, " +
                    $"Message={context.MessageName}, " +
                    $"Depth={context.Depth}");

                // Le plugin met à jour le devis lui-même avec service.Update().
                // Cet Update relance le step sur quote : on l'arrête seulement
                // dans ce cas précis afin d'éviter une boucle infinie.
                if (context.Depth > 1 &&
                    string.Equals(
                        context.PrimaryEntityName,
                        EntityQuote,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        context.MessageName,
                        "Update",
                        StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace(
                        "CalculatePricePlugin: Update récursif de quote détecté, sortie.");
                    return;
                }

                var quoteId = GetQuoteId(context, service, tracing);

                if (quoteId == Guid.Empty)
                {
                    tracing.Trace(
                        "CalculatePricePlugin: devis parent introuvable, sortie.");
                    return;
                }

                RecalculateQuoteTotals(service, tracing, quoteId);

                tracing.Trace("CalculatePricePlugin: fin avec succès.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"CalculatePricePlugin: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans CalculatePricePlugin : {ex.Message}",
                    ex);
            }
        }

        private static Guid GetQuoteId(
            IPluginExecutionContext context,
            IOrganizationService service,
            ITracingService tracing)
        {
            if (string.Equals(
                    context.PrimaryEntityName,
                    EntityQuote,
                    StringComparison.OrdinalIgnoreCase))
            {
                return context.PrimaryEntityId;
            }

            if (!string.Equals(
                    context.PrimaryEntityName,
                    EntityQuoteDetail,
                    StringComparison.OrdinalIgnoreCase))
            {
                tracing.Trace(
                    $"CalculatePricePlugin: entité non gérée : " +
                    $"{context.PrimaryEntityName}");

                return Guid.Empty;
            }

            if (string.Equals(
                    context.MessageName,
                    "Delete",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetQuoteFromImage(
                        context.PreEntityImages,
                        PreImageName,
                        out var deletedLineQuoteId))
                {
                    return deletedLineQuoteId;
                }

                tracing.Trace(
                    "CalculatePricePlugin: PreImage absente ou quoteid absent sur Delete.");

                return Guid.Empty;
            }

            if (context.InputParameters.Contains("Target") &&
                context.InputParameters["Target"] is Entity target)
            {
                var quoteReference = target.GetAttributeValue<EntityReference>(
                    QuoteDetailQuote);

                if (quoteReference != null &&
                    quoteReference.Id != Guid.Empty)
                {
                    return quoteReference.Id;
                }
            }

            if (TryGetQuoteFromImage(
                    context.PreEntityImages,
                    PreImageName,
                    out var preImageQuoteId))
            {
                return preImageQuoteId;
            }

            var quoteDetailId = context.PrimaryEntityId;

            if (quoteDetailId == Guid.Empty &&
                context.OutputParameters.Contains("id") &&
                context.OutputParameters["id"] is Guid createdLineId)
            {
                quoteDetailId = createdLineId;
            }

            if (quoteDetailId != Guid.Empty)
            {
                return RetrieveQuoteIdFromDetail(
                    service,
                    quoteDetailId,
                    tracing);
            }

            tracing.Trace(
                "CalculatePricePlugin: impossible de retrouver le devis parent.");

            return Guid.Empty;
        }

        private static bool TryGetQuoteFromImage(
            EntityImageCollection images,
            string imageName,
            out Guid quoteId)
        {
            quoteId = Guid.Empty;

            if (images == null || !images.Contains(imageName))
                return false;

            var image = images[imageName];

            var quoteReference = image.GetAttributeValue<EntityReference>(
                QuoteDetailQuote);

            if (quoteReference == null || quoteReference.Id == Guid.Empty)
                return false;

            quoteId = quoteReference.Id;
            return true;
        }

        private static Guid RetrieveQuoteIdFromDetail(
            IOrganizationService service,
            Guid quoteDetailId,
            ITracingService tracing)
        {
            var quoteDetail = service.Retrieve(
                EntityQuoteDetail,
                quoteDetailId,
                new ColumnSet(QuoteDetailQuote));

            var quoteReference = quoteDetail.GetAttributeValue<EntityReference>(
                QuoteDetailQuote);

            if (quoteReference == null || quoteReference.Id == Guid.Empty)
            {
                tracing.Trace(
                    $"CalculatePricePlugin: quoteid absent de la ligne {quoteDetailId}.");

                return Guid.Empty;
            }

            return quoteReference.Id;
        }

        private static void RecalculateQuoteTotals(
            IOrganizationService service,
            ITracingService tracing,
            Guid quoteId)
        {
            var quote = service.Retrieve(
                EntityQuote,
                quoteId,
                new ColumnSet(QuoteDiscountPercentage));

            // Le HT est calculé directement depuis les lignes du devis.
            // Cela évite de dépendre du moment où Dynamics met à jour
            // le champ système totallineitemamount.
            var totalHt = GetQuoteLinesTotalHt(
                service,
                tracing,
                quoteId);

            var discountPercentage = GetDecimalValue(
                quote,
                QuoteDiscountPercentage);

            if (discountPercentage < 0m)
                discountPercentage = 0m;

            var discountAmount = RoundMoney(
                totalHt * (discountPercentage / 100m));

            var netHt = RoundMoney(totalHt - discountAmount);

            tracing.Trace(
                $"Devis {quoteId}: HT lignes={totalHt}, " +
                $"Remise%={discountPercentage}, " +
                $"Remise={discountAmount}, " +
                $"NetHT={netHt}");

            var totalTax = CalculateTaxOnNetAmount(
                service,
                tracing,
                quoteId,
                totalHt,
                netHt);

            var totalTtc = RoundMoney(netHt + totalTax);

            var update = new Entity(EntityQuote, quoteId)
            {
                [QuoteDiscountAmount] = new Money(discountAmount),
                [QuoteNetHt] = new Money(netHt),
                [QuoteCustomTax] = new Money(totalTax),
                [QuoteCustomTotal] = new Money(totalTtc)
            };

            service.Update(update);

            tracing.Trace(
                $"Devis {quoteId} recalculé : " +
                $"Remise={discountAmount}, " +
                $"NetHT={netHt}, " +
                $"Taxes={totalTax}, " +
                $"TotalTTC={totalTtc}");
        }

        private static decimal GetQuoteLinesTotalHt(
            IOrganizationService service,
            ITracingService tracing,
            Guid quoteId)
        {
            var query = new QueryExpression(EntityQuoteDetail)
            {
                ColumnSet = new ColumnSet(QuoteDetailBaseAmount)
            };

            query.Criteria.AddCondition(
                QuoteDetailQuote,
                ConditionOperator.Equal,
                quoteId);

            var details = service.RetrieveMultiple(query).Entities;

            decimal totalHt = 0m;

            tracing.Trace(
                $"CalculatePricePlugin: {details.Count} ligne(s) trouvée(s) " +
                $"pour le devis {quoteId}.");

            foreach (var detail in details)
            {
                var baseAmount = GetMoneyValue(
                    detail,
                    QuoteDetailBaseAmount);

                totalHt += baseAmount;

                tracing.Trace(
                    $"Ligne {detail.Id}: " +
                    $"baseamount={baseAmount}, " +
                    $"CumulHT={totalHt}");
            }

            return RoundMoney(totalHt);
        }

        private static decimal CalculateTaxOnNetAmount(
            IOrganizationService service,
            ITracingService tracing,
            Guid quoteId,
            decimal totalHt,
            decimal netHt)
        {
            if (totalHt <= 0m)
            {
                tracing.Trace(
                    "CalculatePricePlugin: total HT nul, taxes fixées à zéro.");

                return 0m;
            }

            var query = new QueryExpression(EntityQuoteDetail)
            {
                ColumnSet = new ColumnSet(
                    QuoteDetailBaseAmount,
                    QuoteDetailTaxRate)
            };

            query.Criteria.AddCondition(
                QuoteDetailQuote,
                ConditionOperator.Equal,
                quoteId);

            var details = service.RetrieveMultiple(query).Entities;

            decimal totalTax = 0m;

            foreach (var detail in details)
            {
                var baseAmount = GetMoneyValue(
                    detail,
                    QuoteDetailBaseAmount);

                var taxRate = GetDecimalValue(
                    detail,
                    QuoteDetailTaxRate);

                if (baseAmount <= 0m)
                {
                    tracing.Trace(
                        $"Ligne {detail.Id} ignorée : baseamount={baseAmount}.");

                    continue;
                }

                if (taxRate <= 0m)
                {
                    tracing.Trace(
                        $"Ligne {detail.Id} ignorée : " +
                        $"{QuoteDetailTaxRate}={taxRate}.");

                    continue;
                }

                var lineNetHt = (baseAmount / totalHt) * netHt;

                var lineTax = RoundMoney(
                    lineNetHt * (taxRate / 100m));

                totalTax += lineTax;

                tracing.Trace(
                    $"Ligne {detail.Id}: " +
                    $"HT={baseAmount}, " +
                    $"Taux={taxRate}, " +
                    $"NetHT={lineNetHt}, " +
                    $"Taxe={lineTax}");
            }

            return RoundMoney(totalTax);
        }

        private static decimal GetMoneyValue(
            Entity entity,
            string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.TryGetValue(
                    attributeName,
                    out var value) ||
                value == null)
            {
                return 0m;
            }

            return value is Money money
                ? money.Value
                : 0m;
        }

        private static decimal GetDecimalValue(
            Entity entity,
            string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.TryGetValue(
                    attributeName,
                    out var value) ||
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

            return decimal.TryParse(
                value.ToString(),
                out var parsedValue)
                ? parsedValue
                : 0m;
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }
    }
}