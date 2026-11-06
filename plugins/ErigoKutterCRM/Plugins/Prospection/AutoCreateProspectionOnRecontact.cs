using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutterCRM.Plugins.Prospection;

public class AutoCreateProspectionOnRecontact : IPlugin
{
	private const int RESULT_A_RECONTACTER = 760660001;

	private const int DEFAULT_DURATION_MINUTES = 30;

	private static readonly HashSet<string> ExcludedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"activityid", "bsc_prospectionid", "statecode", "statuscode", "bsc_prospectionresult", "createdon", "modifiedon", "overriddencreatedon", "actualstart", "actualend",
		"actualdurationminutes", "scheduledstart", "scheduledend", "scheduleddurationminutes", "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby", "versionnumber", "activitytypecode",
		"instancetypecode", "isbilled", "isregularactivity", "isworkflowcreated", "bsc_datearecontacter", "bsc_prospectionyear", "processid", "stageid", "traversedpath", "activityadditionalparams",
		"deliverylastattemptedon", "postponeactivityprocessinguntil", "sendermailboxid", "senton", "seriesid", "sortdate"
	};

	public void Execute(IServiceProvider serviceProvider)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected Obj, but got Unknown
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected Obj, but got Unknown
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Expected Obj, but got Unknown
		//IL_038b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Expected Obj, but got Unknown
		//IL_01c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Expected Obj, but got Unknown
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0223: Expected Obj, but got Unknown
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		IPluginExecutionContext val = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
		IOrganizationServiceFactory val2 = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
		IOrganizationService val3 = val2.CreateOrganizationService((Guid?)((IExecutionContext)val).UserId);
		ITracingService val4 = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
		try
		{
			val4.Trace("=== Plugin AutoCreateProspectionOnRecontact v4 ===", Array.Empty<object>());
			if (!((DataCollection<string, object>)(object)((IExecutionContext)val).InputParameters).Contains("Target"))
			{
				return;
			}
			object obj = ((DataCollection<string, object>)(object)((IExecutionContext)val).InputParameters)["Target"];
			Entity val5 = (Entity)((obj is Entity) ? obj : null);
			if (val5 == null || val5.LogicalName != "bsc_prospection")
			{
				return;
			}
			Entity val6 = (((DataCollection<string, Entity>)(object)((IExecutionContext)val).PostEntityImages).Contains("PostImage") ? ((DataCollection<string, Entity>)(object)((IExecutionContext)val).PostEntityImages)["PostImage"] : val3.Retrieve("bsc_prospection", val5.Id, new ColumnSet(new string[3] { "statecode", "bsc_prospectionresult", "bsc_datearecontacter" })));
			OptionSetValue attributeValue = val6.GetAttributeValue<OptionSetValue>("statecode");
			if (((attributeValue != null) ? new int?(attributeValue.Value) : ((int?)null)) != 1)
			{
				return;
			}
			OptionSetValue attributeValue2 = val6.GetAttributeValue<OptionSetValue>("bsc_prospectionresult");
			if (((attributeValue2 != null) ? new int?(attributeValue2.Value) : ((int?)null)) == 760660001)
			{
				val4.Trace("Condition remplie: création nouvelle prospection...", Array.Empty<object>());
				Entity val7 = val3.Retrieve("bsc_prospection", val5.Id, new ColumnSet(true));
				DateTime? attributeValue3 = val7.GetAttributeValue<DateTime?>("bsc_datearecontacter");
				if (!attributeValue3.HasValue)
				{
					throw new InvalidPluginExecutionException("La date de recontact doit être renseignée.");
				}
				val4.Trace($"[SOURCE] bsc_datearecontacter = {attributeValue3.Value:yyyy-MM-dd HH:mm:ss}", Array.Empty<object>());
				Entity val8 = new Entity("bsc_prospection");
				CopyCurrency(val7, val8, val4);
				int num = CopyAllFields(val7, val8, val4);
				val4.Trace($"[COPIE] {num} champs copiés", Array.Empty<object>());
				DateTime value = attributeValue3.Value;
				DateTime dateTime = value.AddMinutes(30.0);
				val8["scheduledstart"] = value;
				val8["scheduledend"] = dateTime;
				val8["scheduleddurationminutes"] = 30;
				val4.Trace($"[DATES] scheduledstart = {value:yyyy-MM-dd HH:mm:ss}", Array.Empty<object>());
				val4.Trace($"[DATES] scheduledend = {dateTime:yyyy-MM-dd HH:mm:ss}", Array.Empty<object>());
				val8["bsc_prospectionyear"] = value.Year;
				LogBeforeCreate(val8, val4);
				Guid guid = val3.Create(val8);
				val4.Trace($"[SUCCESS] Nouvelle prospection créée: {guid}", Array.Empty<object>());
			}
		}
		catch (InvalidPluginExecutionException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			val4.Trace("[ERREUR] " + ex2.Message, Array.Empty<object>());
			val4.Trace("[STACK] " + ex2.StackTrace, Array.Empty<object>());
			throw new InvalidPluginExecutionException("Erreur: " + ex2.Message, ex2);
		}
	}

	private void CopyCurrency(Entity source, Entity target, ITracingService tracing)
	{
		if (source.Contains("transactioncurrencyid") && source["transactioncurrencyid"] != null)
		{
			target["transactioncurrencyid"] = source["transactioncurrencyid"];
			tracing.Trace("[DEVISE] transactioncurrencyid copié", Array.Empty<object>());
		}
		else
		{
			tracing.Trace("[DEVISE] transactioncurrencyid absent de la source", Array.Empty<object>());
		}
	}

	private int CopyAllFields(Entity source, Entity target, ITracingService tracing)
	{
		int num = 0;
		foreach (KeyValuePair<string, object> item in (DataCollection<string, object>)(object)source.Attributes)
		{
			string key = item.Key;
			if (!ExcludedFields.Contains(key) && item.Value != null && !key.EndsWith("_base", StringComparison.OrdinalIgnoreCase) && !key.Equals("exchangerate", StringComparison.OrdinalIgnoreCase) && !key.Equals("transactioncurrencyid", StringComparison.OrdinalIgnoreCase))
			{
				target[key] = item.Value;
				num++;
				object value = item.Value;
				Money val = (Money)((value is Money) ? value : null);
				if (val != null)
				{
					tracing.Trace($"  [$$] {key} = {val.Value}", Array.Empty<object>());
				}
			}
		}
		return num;
	}

	private void LogBeforeCreate(Entity entity, ITracingService tracing)
	{
		tracing.Trace("=== VÉRIFICATION AVANT CRÉATION ===", Array.Empty<object>());
		if (entity.Contains("bsc_montantestime"))
		{
			object obj = entity["bsc_montantestime"];
			Money val = (Money)((obj is Money) ? obj : null);
			if (val != null)
			{
				tracing.Trace($"  [OK] bsc_montantestime = {val.Value}", Array.Empty<object>());
			}
			else
			{
				tracing.Trace("  [??] bsc_montantestime type = " + obj?.GetType().Name, Array.Empty<object>());
			}
		}
		else
		{
			tracing.Trace("  [ABSENT] bsc_montantestime", Array.Empty<object>());
		}
		if (entity.Contains("transactioncurrencyid"))
		{
			tracing.Trace("  [OK] transactioncurrencyid présent", Array.Empty<object>());
		}
		else
		{
			tracing.Trace("  [ABSENT] transactioncurrencyid", Array.Empty<object>());
		}
		if (entity.Contains("scheduledstart"))
		{
			DateTime attributeValue = entity.GetAttributeValue<DateTime>("scheduledstart");
			tracing.Trace($"  [OK] scheduledstart = {attributeValue:yyyy-MM-dd HH:mm}", Array.Empty<object>());
		}
		if (entity.Contains("scheduledend"))
		{
			DateTime attributeValue2 = entity.GetAttributeValue<DateTime>("scheduledend");
			tracing.Trace($"  [OK] scheduledend = {attributeValue2:yyyy-MM-dd HH:mm}", Array.Empty<object>());
		}
		int num = 0;
		foreach (KeyValuePair<string, object> item in (DataCollection<string, object>)(object)entity.Attributes)
		{
			if (item.Value is Money)
			{
				num++;
			}
		}
		tracing.Trace($"  [INFO] {num} champ(s) Money dans l'entité", Array.Empty<object>());
	}
}
