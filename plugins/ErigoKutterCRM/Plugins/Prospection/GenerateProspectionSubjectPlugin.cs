using DivicoKutterCRM.Plugins.Constants;
using DivicoKutterCRM.Plugins.Helpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Security.Principal;

namespace DivicoKutterCRM.Plugins.Prospection
{
    public class GenerateProspectionSubjectPlugin : IPlugin
    {
        private const string ProspectionEntity = "bsc_prospection";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != ProspectionEntity)
            {
                return;
            }

            if (context.Depth > 1)
            {
                tracing.Trace(
                    "GenerateProspectionSubjectPlugin: arrêt anti-boucle. Depth={0}",
                    context.Depth);

                return;
            }

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(
                typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracing.Trace(
                "GenerateProspectionSubjectPlugin: début. Message={0}, Depth={1}",
                context.MessageName,
                context.Depth);

            var prospection = service.Retrieve(
                ProspectionEntity,
                target.Id,
                new ColumnSet(
                    FieldNames.Subject,
                    FieldNames.Client,
                    FieldNames.Municipalite,
                    FieldNames.ContactPrincipal,
                    FieldNames.ScheduledStart,
                    FieldNames.OwnerId,
                    FieldNames.RegardingObjectId));

            var update = new Entity(ProspectionEntity, prospection.Id);
            var mustUpdate = false;

            var subject = prospection.GetAttributeValue<string>(FieldNames.Subject);

            if (string.IsNullOrWhiteSpace(subject))
            {
                update[FieldNames.Subject] =
                    ProspectionSubjectHelper.GenerateSubject(service, prospection);

                mustUpdate = true;

                tracing.Trace(
                    "GenerateProspectionSubjectPlugin: sujet généré : {0}",
                    update[FieldNames.Subject]);
            }

            var regarding = prospection.GetAttributeValue<EntityReference>(
                FieldNames.RegardingObjectId);

            var municipalite = prospection.GetAttributeValue<EntityReference>(
                FieldNames.Municipalite);

            if (regarding == null && municipalite != null)
            {
                update[FieldNames.RegardingObjectId] =
                    new EntityReference(municipalite.LogicalName, municipalite.Id)
                    {
                        Name = municipalite.Name
                    };

                mustUpdate = true;

                tracing.Trace(
                    "GenerateProspectionSubjectPlugin: Concernant rempli depuis Municipalité : {0}",
                    municipalite.Name);
            }

            if (mustUpdate)
            {
                service.Update(update);
                tracing.Trace(
                    "GenerateProspectionSubjectPlugin: Prospection mise à jour.");
            }
            else
            {
                tracing.Trace(
                    "GenerateProspectionSubjectPlugin: aucune mise à jour nécessaire.");
            }
        }
    }
}