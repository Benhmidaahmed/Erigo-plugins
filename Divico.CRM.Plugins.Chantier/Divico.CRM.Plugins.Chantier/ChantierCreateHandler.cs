using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutter.Plugins
{
    public class ChantierCreateHandler : IPlugin
    {
        private const string EntityChantier = "bsc_chantier";
        private const string EntityDevis = "quote";
        private const string EntityContact = "contact";

        // Champs du chantier
        private const string ChantierDevis = "bsc_devisid";
        private const string ChantierClient = "bsc_client";
        private const string ChantierContact = "bsc_contactid";
        private const string ChantierRegion = "bsc_region";
        private const string ChantierMunicipalite = "bsc_municipalite";
        private const string ChantierOpportunite = "bsc_offreid";
        private const string ChantierNom = "bsc_name";

        // Champs du devis
        private const string DevisClient = "bsc_client";
        private const string DevisContact = "bsc_parentcontactid";
        private const string DevisRegion = "bsc_region";
        private const string DevisOpportunite = "opportunityid";
        private const string DevisNom = "name";

        // Champ du contact
        private const string ContactMunicipalite = "bsc_municipalite";

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
                    EntityChantier,
                    StringComparison.OrdinalIgnoreCase)
                || context.Stage != 20)
            {
                return;
            }

            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            Entity chantier =
                context.InputParameters["Target"] as Entity;

            if (chantier == null ||
                chantier.LogicalName != EntityChantier)
            {
                return;
            }

            try
            {
                tracing.Trace(
                    "ChantierCreateHandler : début. Stage={0}, Depth={1}.",
                    context.Stage,
                    context.Depth);

                EntityReference devisReference =
                    chantier.GetAttributeValue<EntityReference>(
                        ChantierDevis);

                if (devisReference == null)
                {
                    tracing.Trace(
                        "ChantierCreateHandler : aucun devis lié, " +
                        "aucune copie effectuée.");
                    return;
                }

                if (!string.Equals(
                        devisReference.LogicalName,
                        EntityDevis,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        "L'offre d'origine doit pointer vers un devis.");
                }

                IOrganizationServiceFactory factory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(
                        typeof(IOrganizationServiceFactory));

                IOrganizationService service =
                    factory.CreateOrganizationService(context.UserId);

                Entity devis = service.Retrieve(
                    EntityDevis,
                    devisReference.Id,
                    new ColumnSet(
                        DevisClient,
                        DevisContact,
                        DevisRegion,
                        DevisOpportunite,
                        DevisNom));

                tracing.Trace(
                    "ChantierCreateHandler : devis lu, Id={0}.",
                    devisReference.Id);

                // Copie du client : le champ Recherche affichera son nom.
                CopyLookupIfEmpty(
                    devis,
                    DevisClient,
                    chantier,
                    ChantierClient,
                    tracing);

                CopyLookupIfEmpty(
                    devis,
                    DevisContact,
                    chantier,
                    ChantierContact,
                    tracing);

                CopyLookupIfEmpty(
                    devis,
                    DevisRegion,
                    chantier,
                    ChantierRegion,
                    tracing);

                CopyLookupIfEmpty(
                    devis,
                    DevisOpportunite,
                    chantier,
                    ChantierOpportunite,
                    tracing);

                // Règle B : préfixe "Chantier | " suivi du nom du devis.
                if (IsEmpty(chantier, ChantierNom))
                {
                    string devisName =
                        devis.GetAttributeValue<string>(DevisNom);

                    if (string.IsNullOrWhiteSpace(devisName))
                    {
                        throw new InvalidPluginExecutionException(
                            "Le devis d'origine ne possède pas de nom. " +
                            "Impossible de générer le nom du chantier.");
                    }

                    string chantierName =
                        "Chantier | " + devisName.Trim();

                    chantier[ChantierNom] = chantierName;

                    tracing.Trace(
                        "ChantierCreateHandler : nom généré, longueur={0}.",
                        chantierName.Length);
                }
                else
                {
                    tracing.Trace(
                        "ChantierCreateHandler : nom existant conservé.");
                }

                // Lecture de la municipalité depuis le contact retenu.
                EntityReference contactReference =
                    chantier.GetAttributeValue<EntityReference>(
                        ChantierContact);

                if (IsEmpty(chantier, ChantierMunicipalite))
                {
                    if (contactReference == null)
                    {
                        tracing.Trace(
                            "ChantierCreateHandler : aucun contact, " +
                            "municipalité non copiée.");
                    }
                    else
                    {
                        if (!string.Equals(
                                contactReference.LogicalName,
                                EntityContact,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidPluginExecutionException(
                                "Le contact principal du chantier doit " +
                                "pointer vers la table Contact.");
                        }

                        Entity contact = service.Retrieve(
                            EntityContact,
                            contactReference.Id,
                            new ColumnSet(ContactMunicipalite));

                        CopyLookupIfEmpty(
                            contact,
                            ContactMunicipalite,
                            chantier,
                            ChantierMunicipalite,
                            tracing);
                    }
                }
                else
                {
                    tracing.Trace(
                        "ChantierCreateHandler : municipalité " +
                        "existante conservée.");
                }

                tracing.Trace(
                    "ChantierCreateHandler : fin avec succès.");
            }
            catch (InvalidPluginExecutionException ex)
            {
                tracing.Trace(
                    "ChantierCreateHandler : erreur métier : {0}",
                    ex.ToString());

                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    "ChantierCreateHandler : erreur technique : {0}",
                    ex.ToString());

                throw new InvalidPluginExecutionException(
                    "Impossible de compléter le chantier à partir " +
                    "du devis et du contact. " +
                    "Consultez les traces du plugin.",
                    ex);
            }
        }

        private static void CopyLookupIfEmpty(
            Entity source,
            string sourceAttribute,
            Entity destination,
            string destinationAttribute,
            ITracingService tracing)
        {
            if (!IsEmpty(destination, destinationAttribute))
            {
                tracing.Trace(
                    "ChantierCreateHandler : {0} déjà renseigné, " +
                    "valeur conservée.",
                    destinationAttribute);

                return;
            }

            EntityReference reference =
                source.GetAttributeValue<EntityReference>(
                    sourceAttribute);

            if (reference == null)
            {
                tracing.Trace(
                    "ChantierCreateHandler : source {0}.{1} vide.",
                    source.LogicalName,
                    sourceAttribute);

                return;
            }

            destination[destinationAttribute] =
                new EntityReference(
                    reference.LogicalName,
                    reference.Id);

            tracing.Trace(
                "ChantierCreateHandler : {0}.{1} copié vers {2}.{3}.",
                source.LogicalName,
                sourceAttribute,
                destination.LogicalName,
                destinationAttribute);
        }

        private static bool IsEmpty(
            Entity entity,
            string attributeName)
        {
            if (!entity.Attributes.Contains(attributeName) ||
                entity[attributeName] == null)
            {
                return true;
            }

            string text = entity[attributeName] as string;

            return text != null &&
                   string.IsNullOrWhiteSpace(text);
        }
    }
}