using System;
using System.Collections.ObjectModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutterCRM.Plugins.Helpers;

public static class ActivityOverlapValidator
{
	public static void Validate(IOrganizationService service, Entity target, Guid ownerId)
	{
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Expected Obj, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Expected Obj, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected Obj, but got Unknown
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected Obj, but got Unknown
		//IL_00d8: Expected Obj, but got Unknown
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		if (target.Contains("scheduledstart") && target.Contains("scheduledend"))
		{
			DateTime attributeValue = target.GetAttributeValue<DateTime>("scheduledstart");
			DateTime attributeValue2 = target.GetAttributeValue<DateTime>("scheduledend");
			if (attributeValue2 <= attributeValue)
			{
				throw new InvalidPluginExecutionException("La date de fin doit être postérieure à la date de début.");
			}
			QueryExpression val = new QueryExpression("activitypointer")
			{
				ColumnSet = new ColumnSet(false)
			};
			((Collection<ConditionExpression>)(object)val.Criteria.Conditions).Add(new ConditionExpression("ownerid", (ConditionOperator)0, (object)ownerId));
			((Collection<ConditionExpression>)(object)val.Criteria.Conditions).Add(new ConditionExpression("scheduledstart", (ConditionOperator)3, (object)attributeValue2));
			((Collection<ConditionExpression>)(object)val.Criteria.Conditions).Add(new ConditionExpression("scheduledend", (ConditionOperator)2, (object)attributeValue));
			QueryExpression val2 = val;
			EntityCollection val3 = service.RetrieveMultiple((QueryBase)(object)val2);
			if (((Collection<Entity>)(object)val3.Entities).Count > 0)
			{
				throw new InvalidPluginExecutionException("Une autre activité est déjà planifiée sur cette plage horaire. Veuillez vérifier votre agenda.");
			}
		}
	}
}
