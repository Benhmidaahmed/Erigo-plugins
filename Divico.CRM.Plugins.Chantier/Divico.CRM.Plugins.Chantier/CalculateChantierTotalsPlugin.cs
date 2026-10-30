using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutter.Plugins
{
    /// <summary>
    /// Recalcule les totaux réalisés d'un chantier à partir de ses produits chantier.
    ///
    /// Calcul :
    /// Total HT réalisé = somme de bsc_montant_realise
    /// Montant remise = Total HT × pourcentage de remise
    /// Net HT = Total HT − montant remise
    /// Taxes = taxes calculées sur le Net HT
    /// Total TTC = Net HT + taxes
    ///
    /// Enregistrement recommandé :
    /// - bsc_chantier : Create et Update
    /// - bsc_produit_chantier : Create, Update et Delete
    /// - Stage : PostOperation
    /// - Mode : Synchronous
    /// </summary>
    public class CalculateChantierTotalsPlugin : IPlugin
    {
        private const string EntityChantier = "bsc_chantier";
        private const string EntityProduitChantier = "bsc_produit_chantier";

        // Relations
        private const string ProduitChantierLookup = "bsc_chantier";

        // Champs Produits chantier
        private const string ProduitMontantRealise = "bsc_montant_realise";
        private const string ProduitTauxTaxe = "bsc_taxepercentage";

        // Champs Chantier
        private const string ChantierRemisePourcentage = "bsc_discountpercentage";
        private const string ChantierTotalHt = "bsc_totallineitemamount";
        private const string ChantierTaxes = "bsc_customtax";
        private const string ChantierTotalTtc = "bsc_customtotal";

        private const string PreImageName = "PreImage";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(
                typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            try
            {
                tracing.Trace(
                    $"CalculateChantierTotalsPlugin: début. " +
                    $"Entité={context.PrimaryEntityName}, " +
                    $"Message={context.MessageName}, Depth={context.Depth}");

                if (context.Depth > 2)
                {
                    tracing.Trace("CalculateChantierTotalsPlugin: arrêt anti-boucle.");
                    return;
                }

                var chantierId = GetChantierId(context, service, tracing);

                if (chantierId == Guid.Empty)
                {
                    tracing.Trace(
                        "CalculateChantierTotalsPlugin: ID du chantier introuvable.");

                    return;
                }

                RecalculateTotals(service, tracing, chantierId);

                tracing.Trace("CalculateChantierTotalsPlugin: fin avec succès.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"CalculateChantierTotalsPlugin: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans CalculateChantierTotalsPlugin : {ex.Message}",
                    ex);
            }
        }

        private static Guid GetChantierId(
            IPluginExecutionContext context,
            IOrganizationService service,
            ITracingService tracing)
        {
            // Événement directement sur Chantier.
            if (string.Equals(
                    context.PrimaryEntityName,
                    EntityChantier,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(
                        context.MessageName,
                        "Create",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (context.OutputParameters.Contains("id") &&
                        context.OutputParameters["id"] is Guid createdId)
                    {
                        return createdId;
                    }

                    return context.PrimaryEntityId;
                }

                return context.PrimaryEntityId;
            }

            // Événement sur Produit chantier.
            if (!string.Equals(
                    context.PrimaryEntityName,
                    EntityProduitChantier,
                    StringComparison.OrdinalIgnoreCase))
            {
                tracing.Trace(
                    $"Entité non prise en charge : {context.PrimaryEntityName}");

                return Guid.Empty;
            }

            if (string.Equals(
                    context.MessageName,
                    "Delete",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetChantierFromImage(
                        context.PreEntityImages,
                        PreImageName,
                        out var deletedChantierId))
                {
                    return deletedChantierId;
                }

                if (context.InputParameters.Contains("Target") &&
                    context.InputParameters["Target"] is EntityReference deletedProductRef)
                {
                    return RetrieveChantierId(service, deletedProductRef.Id);
                }

                return Guid.Empty;
            }

            if (context.InputParameters.Contains("Target") &&
                context.InputParameters["Target"] is Entity target)
            {
                var targetChantier =
                    target.GetAttributeValue<EntityReference>(
                        ProduitChantierLookup);

                if (targetChantier != null)
                    return targetChantier.Id;

                if (TryGetChantierFromImage(
                        context.PreEntityImages,
                        PreImageName,
                        out var imageChantierId))
                {
                    return imageChantierId;
                }

                if (target.Id != Guid.Empty)
                    return RetrieveChantierId(service, target.Id);
            }

            return Guid.Empty;
        }

        private static bool TryGetChantierFromImage(
            EntityImageCollection images,
            string imageName,
            out Guid chantierId)
        {
            chantierId = Guid.Empty;

            if (images == null || !images.Contains(imageName))
                return false;

            var image = images[imageName];
            var chantierRef =
                image.GetAttributeValue<EntityReference>(ProduitChantierLookup);

            if (chantierRef == null)
                return false;

            chantierId = chantierRef.Id;
            return chantierId != Guid.Empty;
        }

        private static Guid RetrieveChantierId(
            IOrganizationService service,
            Guid produitChantierId)
        {
            if (produitChantierId == Guid.Empty)
                return Guid.Empty;

            var produit = service.Retrieve(
                EntityProduitChantier,
                produitChantierId,
                new ColumnSet(ProduitChantierLookup));

            var chantierRef =
                produit.GetAttributeValue<EntityReference>(ProduitChantierLookup);

            return chantierRef?.Id ?? Guid.Empty;
        }

        private static void RecalculateTotals(
            IOrganizationService service,
            ITracingService tracing,
            Guid chantierId)
        {
            var chantier = service.Retrieve(
                EntityChantier,
                chantierId,
                new ColumnSet(ChantierRemisePourcentage));

            var totalHt = CalculateTotalHt(service, chantierId);
            var discountPercentage =
                GetDecimalValue(chantier, ChantierRemisePourcentage);

            if (discountPercentage < 0m)
                discountPercentage = 0m;

            // Convention supposée : 5 signifie 5 %.
            // Si Dataverse stocke 0.05 pour 5 %, adapter la formule.
            var discountAmount =
                RoundMoney(totalHt * discountPercentage / 100m);

            var netHt = RoundMoney(totalHt - discountAmount);

            var totalTax =
                CalculateTaxes(service, tracing, chantierId, totalHt, netHt);

            var totalTtc = RoundMoney(netHt + totalTax);

            tracing.Trace(
                $"Chantier={chantierId}; HT={totalHt}; " +
                $"Remise%={discountPercentage}; Remise={discountAmount}; " +
                $"NetHT={netHt}; Taxes={totalTax}; TTC={totalTtc}");

            var update = new Entity(EntityChantier, chantierId)
            {
                [ChantierTotalHt] = new Money(totalHt),
                [ChantierTaxes] = new Money(totalTax),
                [ChantierTotalTtc] = new Money(totalTtc)
            };

            service.Update(update);
        }

        private static decimal CalculateTotalHt(
            IOrganizationService service,
            Guid chantierId)
        {
            var query = new QueryExpression(EntityProduitChantier)
            {
                ColumnSet = new ColumnSet(ProduitMontantRealise)
            };

            query.Criteria.AddCondition(
                ProduitChantierLookup,
                ConditionOperator.Equal,
                chantierId);

            var products = service.RetrieveMultiple(query).Entities;

            decimal total = 0m;

            foreach (var product in products)
                total += GetMoneyValue(product, ProduitMontantRealise);

            return RoundMoney(total);
        }

        private static decimal CalculateTaxes(
            IOrganizationService service,
            ITracingService tracing,
            Guid chantierId,
            decimal totalHt,
            decimal netHt)
        {
            if (totalHt <= 0m)
                return 0m;

            var query = new QueryExpression(EntityProduitChantier)
            {
                ColumnSet = new ColumnSet(
                    ProduitMontantRealise,
                    ProduitTauxTaxe)
            };

            query.Criteria.AddCondition(
                ProduitChantierLookup,
                ConditionOperator.Equal,
                chantierId);

            var products = service.RetrieveMultiple(query).Entities;
            decimal totalTax = 0m;

            foreach (var product in products)
            {
                var amount = GetMoneyValue(product, ProduitMontantRealise);
                var taxRate = GetDecimalValue(product, ProduitTauxTaxe);

                if (amount <= 0m || taxRate <= 0m)
                    continue;

                // Répartit la remise globale au prorata de chaque produit.
                var netAmount = amount / totalHt * netHt;

                // Convention supposée : 5 signifie 5 %.
                var tax = RoundMoney(netAmount * taxRate / 100m);
                totalTax += tax;

                tracing.Trace(
                    $"Produit={product.Id}; Montant={amount}; " +
                    $"Taux={taxRate}; Net={netAmount}; Taxe={tax}");
            }

            return RoundMoney(totalTax);
        }

        private static decimal GetMoneyValue(Entity entity, string attributeName)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var value) ||
                value == null)
            {
                return 0m;
            }

            return value is Money money ? money.Value : 0m;
        }

        private static decimal GetDecimalValue(Entity entity, string attributeName)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var value) ||
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

            return decimal.TryParse(value.ToString(), out var parsedValue)
                ? parsedValue
                : 0m;
        }

        private static decimal RoundMoney(decimal amount)
        {
            return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }
    }
}