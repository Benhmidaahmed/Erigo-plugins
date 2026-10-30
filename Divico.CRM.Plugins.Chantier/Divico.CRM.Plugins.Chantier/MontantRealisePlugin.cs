using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutter.Plugins
{
    /// <summary>
    /// Calcule le montant réalisé d'une ligne de rapport,
    /// puis met à jour les cumuls du produit chantier associé.
    ///
    /// Enregistrement recommandé :
    /// - Table : bsc_produit_rapport
    /// - Messages : Create, Update
    /// - Stage : PostOperation
    /// - Mode : Synchronous
    /// - Filtrage Update : bsc_qte_realisee, bsc_prix_unitaire,
    ///   bsc_taxepercentage, bsc_produit_chantier
    /// </summary>
    public class MontantRealisePlugin : IPlugin
    {
        private const string EntityProduitRapport = "bsc_produit_rapport";
        private const string EntityProduitChantier = "bsc_produit_chantier";

        // Lookup du produit chantier sur la ligne du rapport
        private const string ProduitRapportProduitChantier =
            "bsc_produit_chantier";

        // Champs de bsc_produit_rapport
        private const string ProduitRapportQuantite = "bsc_qte_realisee";
        private const string ProduitRapportPrixUnitaire = "bsc_prix_unitaire";
        private const string ProduitRapportTauxTaxe = "bsc_taxepercentage";
        private const string ProduitRapportMontantRealise = "bsc_montant_realise";

        // Champs de bsc_produit_chantier
        private const string ProduitChantierQuantiteCumulee =
            "bsc_qte_realisee_total";

        private const string ProduitChantierMontantRealise =
            "bsc_montant_realise";

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
                    $"MontantRealisePlugin: début. " +
                    $"Entity={context.PrimaryEntityName}, " +
                    $"Message={context.MessageName}, Depth={context.Depth}");

                if (!string.Equals(
                        context.PrimaryEntityName,
                        EntityProduitRapport,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!string.Equals(
                        context.MessageName,
                        "Create",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        context.MessageName,
                        "Update",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (context.Depth > 2)
                {
                    tracing.Trace("MontantRealisePlugin: arrêt anti-boucle.");
                    return;
                }

                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity target))
                {
                    tracing.Trace("MontantRealisePlugin: Target absent ou invalide.");
                    return;
                }

                if (target.Id == Guid.Empty)
                {
                    tracing.Trace(
                        "MontantRealisePlugin: ID de la ligne produit rapport introuvable.");

                    return;
                }

                // Relire les valeurs finales après l'opération.
                var produitRapport = service.Retrieve(
                    EntityProduitRapport,
                    target.Id,
                    new ColumnSet(
                        ProduitRapportProduitChantier,
                        ProduitRapportQuantite,
                        ProduitRapportPrixUnitaire,
                        ProduitRapportTauxTaxe,
                        ProduitRapportMontantRealise));

                var produitChantierRef =
                    produitRapport.GetAttributeValue<EntityReference>(
                        ProduitRapportProduitChantier);

                if (produitChantierRef == null)
                {
                    tracing.Trace(
                        "MontantRealisePlugin: lookup bsc_produit_chantier absent.");

                    return;
                }

                var quantite = GetDecimalValue(
                    produitRapport,
                    ProduitRapportQuantite);

                var prixUnitaire = GetMoneyValue(
                    produitRapport,
                    ProduitRapportPrixUnitaire);

                var tauxTaxe = GetDecimalValue(
                    produitRapport,
                    ProduitRapportTauxTaxe);

                var montantRealise = CalculateAmount(
                    prixUnitaire,
                    quantite,
                    tauxTaxe);

                tracing.Trace(
                    $"MontantRealisePlugin: quantité={quantite}, " +
                    $"prix unitaire={prixUnitaire}, taux taxe={tauxTaxe}, " +
                    $"montant réalisé TTC={montantRealise}");

                // Éviter une mise à jour inutile, qui pourrait relancer le plugin.
                var montantExistant = GetMoneyValue(
                    produitRapport,
                    ProduitRapportMontantRealise);

                if (montantExistant != montantRealise)
                {
                    var updateProduitRapport =
                        new Entity(EntityProduitRapport, produitRapport.Id);

                    updateProduitRapport[ProduitRapportMontantRealise] =
                        new Money(montantRealise);

                    service.Update(updateProduitRapport);
                }

                // Recalculer les cumuls de toutes les lignes de rapport
                // reliées à ce même produit chantier.
                RecalculateProduitChantier(
                    service,
                    tracing,
                    produitChantierRef.Id);

                tracing.Trace("MontantRealisePlugin: fin avec succès.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"MontantRealisePlugin: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans MontantRealisePlugin : {ex.Message}",
                    ex);
            }
        }

        private static void RecalculateProduitChantier(
            IOrganizationService service,
            ITracingService tracing,
            Guid produitChantierId)
        {
            var query = new QueryExpression(EntityProduitRapport)
            {
                ColumnSet = new ColumnSet(
                    ProduitRapportQuantite,
                    ProduitRapportPrixUnitaire,
                    ProduitRapportTauxTaxe)
            };

            query.Criteria.AddCondition(
                ProduitRapportProduitChantier,
                ConditionOperator.Equal,
                produitChantierId);

            var lignesRapport = service.RetrieveMultiple(query).Entities;

            decimal quantiteTotale = 0m;
            decimal montantTotalRealise = 0m;

            foreach (var ligne in lignesRapport)
            {
                var quantite = GetDecimalValue(
                    ligne,
                    ProduitRapportQuantite);

                var prixUnitaire = GetMoneyValue(
                    ligne,
                    ProduitRapportPrixUnitaire);

                var tauxTaxe = GetDecimalValue(
                    ligne,
                    ProduitRapportTauxTaxe);

                quantiteTotale += quantite;

                montantTotalRealise += CalculateAmount(
                    prixUnitaire,
                    quantite,
                    tauxTaxe);
            }

            quantiteTotale = RoundValue(quantiteTotale);
            montantTotalRealise = RoundValue(montantTotalRealise);

            tracing.Trace(
                $"MontantRealisePlugin: produit chantier={produitChantierId}, " +
                $"quantité cumulée={quantiteTotale}, " +
                $"montant cumulé TTC={montantTotalRealise}");

            var produitChantier = service.Retrieve(
                EntityProduitChantier,
                produitChantierId,
                new ColumnSet(
                    ProduitChantierQuantiteCumulee,
                    ProduitChantierMontantRealise));

            var ancienneQuantite = GetDecimalValue(
                produitChantier,
                ProduitChantierQuantiteCumulee);

            var ancienMontant = GetMoneyValue(
                produitChantier,
                ProduitChantierMontantRealise);

            if (ancienneQuantite == quantiteTotale &&
                ancienMontant == montantTotalRealise)
            {
                tracing.Trace(
                    "MontantRealisePlugin: cumuls du produit chantier déjà à jour.");

                return;
            }

            var updateProduitChantier =
                new Entity(EntityProduitChantier, produitChantierId);

            updateProduitChantier[ProduitChantierQuantiteCumulee] =
                quantiteTotale;

            updateProduitChantier[ProduitChantierMontantRealise] =
                new Money(montantTotalRealise);

            service.Update(updateProduitChantier);
        }

        private static decimal CalculateAmount(
            decimal prixUnitaire,
            decimal quantite,
            decimal tauxTaxe)
        {
            // Hypothèse : le taux est exprimé en pourcentage,
            // par exemple 5 pour 5 %.
            var montantHt = prixUnitaire * quantite;
            var montantTtc = montantHt * (1m + tauxTaxe / 100m);

            return RoundValue(montantTtc);
        }

        private static decimal GetMoneyValue(
            Entity entity,
            string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.TryGetValue(attributeName, out var value) ||
                value == null)
            {
                return 0m;
            }

            return value is Money money ? money.Value : 0m;
        }

        private static decimal GetDecimalValue(
            Entity entity,
            string attributeName)
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

            return decimal.TryParse(value.ToString(), out var parsedValue)
                ? parsedValue
                : 0m;
        }

        private static decimal RoundValue(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}