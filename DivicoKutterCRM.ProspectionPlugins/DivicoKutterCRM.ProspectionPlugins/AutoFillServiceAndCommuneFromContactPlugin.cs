using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutterCRM.ProspectionPlugins
{
    /// <summary>
    /// Copie les informations du Contact principal vers la Prospection.
    ///
    /// Mappings :
    /// - Contact.bsc_client -> Prospection.bsc_client
    /// - Contact.bsc_municipalite -> Prospection.bsc_municipalite
    /// - Contact.jobtitle -> Prospection.bsc_fonction
    /// - Contact.bsc_region -> Prospection.bsc_region
    ///
    /// S'exécute sur Create et Update.
    /// Pour Update, la step est filtrée sur bsc_contactprincipal.
    /// </summary>
    public class AutoFillServiceAndCommuneFromContactPlugin : IPlugin
    {
        private const string EntityProspection = "bsc_prospection";
        private const string EntityContact = "contact";

        // Colonnes Prospection.
        private const string ProspectionContactPrincipal =
            "bsc_contactprincipal";

        private const string ProspectionClient =
            "bsc_client";

        private const string ProspectionMunicipalite =
            "bsc_municipalite";

        private const string ProspectionFonction =
            "bsc_fonction";

        private const string ProspectionRegion =
            "bsc_region";

        // Colonnes Contact.
        private const string ContactClient =
            "bsc_client";

        private const string ContactMunicipalite =
            "bsc_municipalite";

        private const string ContactJobTitle =
            "jobtitle";

        private const string ContactRegion =
            "bsc_region";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            var tracing =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            try
            {
                // Étape 1 : accepter Create ET Update.
                bool isCreate = string.Equals(
                    context.MessageName,
                    "Create",
                    StringComparison.OrdinalIgnoreCase);

                bool isUpdate = string.Equals(
                    context.MessageName,
                    "Update",
                    StringComparison.OrdinalIgnoreCase);

                if (!isCreate && !isUpdate)
                {
                    return;
                }

                // Étape 2 : vérifier la table.
                if (!string.Equals(
                    context.PrimaryEntityName,
                    EntityProspection,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Étape 3 : le code est prévu pour PreOperation synchrone.
                if (context.Stage != 20 || context.Mode != 0)
                {
                    throw new InvalidPluginExecutionException(
                        "Ce plugin doit être enregistré en PreOperation " +
                        "et en mode Synchronous.");
                }

                // Étape 4 : obtenir la Prospection en cours de création/modification.
                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity))
                {
                    tracing.Trace("AutoFill: Target absent ou invalide.");
                    return;
                }

                var target = (Entity)context.InputParameters["Target"];

                if (!string.Equals(
                    target.LogicalName,
                    EntityProspection,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Étape 5 : sur Update, ne travailler que si le
                // Contact principal fait partie de la modification.
                if (isUpdate &&
                    !target.Attributes.Contains(
                        ProspectionContactPrincipal))
                {
                    tracing.Trace(
                        "AutoFill: Update ignoré ; " +
                        "bsc_contactprincipal n'est pas modifié.");

                    return;
                }

                // Étape 6 : lire le Contact principal.
                var contactRef =
                    target.GetAttributeValue<EntityReference>(
                        ProspectionContactPrincipal);

                // Cas où l'utilisateur retire le Contact principal.
                if (contactRef == null)
                {
                    if (isUpdate)
                    {
                        target[ProspectionClient] = null;
                        target[ProspectionMunicipalite] = null;
                        target[ProspectionFonction] = null;
                        target[ProspectionRegion] = null;

                        tracing.Trace(
                            "AutoFill: Contact principal supprimé ; " +
                            "les champs dérivés ont été vidés.");
                    }
                    else
                    {
                        tracing.Trace(
                            "AutoFill: Contact principal absent à la création.");
                    }

                    return;
                }

                if (!string.Equals(
                    contactRef.LogicalName,
                    EntityContact,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        "Le champ bsc_contactprincipal doit référencer " +
                        "un enregistrement de type contact.");
                }

                // Étape 7 : créer le service et lire le Contact.
                var serviceFactory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(
                        typeof(IOrganizationServiceFactory));

                var service =
                    serviceFactory.CreateOrganizationService(
                        context.UserId);

                var contact = service.Retrieve(
                    EntityContact,
                    contactRef.Id,
                    new ColumnSet(
                        ContactClient,
                        ContactMunicipalite,
                        ContactJobTitle,
                        ContactRegion));

                // Étape 8 : copier le Client.
                var clientRef =
                    contact.GetAttributeValue<EntityReference>(
                        ContactClient);

                target[ProspectionClient] = clientRef;

                // Étape 9 : copier la Municipalité.
                var municipaliteRef =
                    contact.GetAttributeValue<EntityReference>(
                        ContactMunicipalite);

                target[ProspectionMunicipalite] = municipaliteRef;

                // Étape 10 : copier la Fonction.
                string jobTitle =
                    contact.GetAttributeValue<string>(
                        ContactJobTitle);

                target[ProspectionFonction] =
                    string.IsNullOrWhiteSpace(jobTitle)
                        ? null
                        : jobTitle;

                // Étape 11 : copier la Région.
                // La valeur est mise à null si elle est absente du Contact.
                if (contact.Attributes.Contains(ContactRegion) &&
                    contact[ContactRegion] != null)
                {
                    target[ProspectionRegion] =
                        contact[ContactRegion];
                }
                else
                {
                    target[ProspectionRegion] = null;
                }

                tracing.Trace(
                    "AutoFill: synchronisation terminée. " +
                    "Message={0}, Contact={1}.",
                    context.MessageName,
                    contactRef.Id);

                // Ne PAS appeler service.Update(target).
                // En PreOperation, les valeurs ajoutées à Target sont
                // enregistrées avec l'opération actuelle.
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    "AutoFill: erreur. CorrelationId={0}. Exception={1}",
                    context.CorrelationId,
                    ex);

                throw new InvalidPluginExecutionException(
                    "Erreur lors de la copie des données du Contact " +
                    "principal vers la Prospection. Détail : " +
                    ex.Message,
                    ex);
            }
        }
    }
}