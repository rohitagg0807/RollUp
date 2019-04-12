/* Description:
VSTS RollUp is a web service which provides summed values of select fields for all child work items of a parent.
Most project managers are interested in getting rollup of estimated or completed work, effort etc.
Hence RollUp will automate the process of summing up the Effort fields, viz., Total Work, Remaining Work and Completed Work from child work item Tasks and save it in the required parent fields, in the same fashion show summation of effort fields of all child Requirements(PBI/User Story) at Feature level and continues for Epic level. 
Before starting with this Azure Function you will need to create the following items: 
  •	Creates 4 Service Hooks viz.,
      1. Work Item (Task) Field Updation (Total Work)
      2. Work Item (Task) Field Updation (Remianing Work)
      3. Work Item (Task) Creation (When any link is added/removed)
      4. Work Item (Any) Field Updation (When link is added/removed)


Before You Start:
Run through the script and change all the instances of Remaining Work, Total Work, Completed Work field query with those exist in your system. For eg FOR TotalWork your field query can be something different from [Custom.TotalWork] so change that.

Note: You can find the query values by making a query of those fields and save in .wiq file. (Client: Visual Studio can be used).

Fucntion Working:
Step 1:
The function will receive the event based on above triggers and will firstly try to find out the child items of work item from which trigger is received.
If child items are found for that WorkItem  summation of the Effort fields 
viz., Total Work, Remaining Work and Completed Work from child work item is done and is updated in WorkItem from which is trigger is received.
If child items are not found then only *fields validation of effort fields are done. 
Step2:
After Step1 parent of WorkItem from which trigger is received is found. If parent is found then from that parent, its childs are found and efforts field are summed up and updated in parent of WorkItem.
The step2 is performed recursively till the highest level parent WorkItem is not found.  

*fields validation:
If only Remaining Work is filled :  Remaining Work is copied into Total Work and Completed Work is set as 0
If only Total Work is filled :   Total Work is copied into Remaining Work if state is not Done else Total Work is copied into Completed Work
If any 2 or 3 fields of above are filled : Then fields are validated and Updated according to following formula:
                                              Completed Work = Total Work - Remaining Work


Changes in Scrum Process:

Total Work and Completed Work fields are added after inherited from the default scrum process.
Total Work, Completed Work and Remaining Work fields are disabled in all those work items which can act as parent( eg PBI, Feature, Epic, Bug)
Completed Work is disabled in Task as it will be handled and filled in by this function

Note: This rollup and field validations are for only those work items which have some parents/links. All the orphan WorkItems which exists by themselves are not affected by this RollUp Service. 
*/
#r "Newtonsoft.Json"
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Configuration;


public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    int workItemId;
    string fieldTotWork = "";
    string fieldComWork = "";
    string fieldRemWork = "";
    string parentID = "";
    string pathTotWork = "/fields/Custom.TotalWork";
    string pathRemWork = "/fields/Microsoft.VSTS.Scheduling.RemainingWork";
    string pathComWork = "/fields/Microsoft.VSTS.Scheduling.CompletedWork";
    string jsonTotWork = "Custom.TotalWork";
    string jsonRemWork = "Microsoft.VSTS.Scheduling.RemainingWork";
    string jsonComWork = "Microsoft.VSTS.Scheduling.CompletedWork";
    string orgname = "https://qrk.visualstudio.com/_apis/wit/workitems/";
    
    List<string> ChildIds = new List<string>();
    try
    {
        //Read JSON response fron the event triggered by service hook by default
        dynamic data = await req.Content.ReadAsAsync<object>();

        // log.Info("Data Received: " + data.ToString());

        dynamic jObject = JsonConvert.DeserializeObject(data.ToString());
        string personalaccesstoken = Environment.GetEnvironmentVariable("FUNCTION_PAT");
        //log.Info(personalaccesstoken);
        //Authenticate against VSTS Server::        
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(
                System.Text.ASCIIEncoding.ASCII.GetBytes(
                    string.Format("{0}:{1}", "", personalaccesstoken))));

        // Reading WorkItem Id, Treating it as parent (as we don't what exactly it is) and trying to find its childs))
        if ((jObject["eventType"]).ToString() == "workitem.updated")
        {
            if ((jObject["resource"]["revision"]["fields"]["System.WorkItemType"]) != null && ((jObject["resource"]["revision"]["fields"]["System.WorkItemType"]).ToString() == "Test Case" || (jObject["resource"]["revision"]["fields"]["System.WorkItemType"]).ToString() == "Impediment"))
            {
                return req.CreateResponse(HttpStatusCode.OK);
            }
            else
            {
                workItemId = Int32.Parse((jObject["resource"]["workItemId"]).ToString());
                //log.Info("hello");
                ChildIds = GetChildItems(jObject);
                if (ChildIds.Count != 0)
                {
                    //log.Info ("No Parent Found");

                    if (jObject["resource"]["revision"]["fields"][jsonTotWork] != null)
                    {
                        fieldTotWork = (jObject["resource"]["revision"]["fields"][jsonTotWork]).ToString("0.##");
                        //log.Info(fieldTotWork);
                    }

                    if (jObject["resource"]["revision"]["fields"][jsonComWork] != null)
                    {
                        fieldComWork = (jObject["resource"]["revision"]["fields"][jsonComWork]).ToString("0.##");
                        //log.Info(fieldComWork);
                    }

                    if (jObject["resource"]["revision"]["fields"][jsonRemWork] != null)
                    {
                        fieldRemWork = (jObject["resource"]["revision"]["fields"][jsonRemWork]).ToString("0.##");
                        // log.Info(fieldRemWork);
                    }
                    string childURL = ChildInfURL(orgname, ChildIds, jsonTotWork, jsonRemWork, jsonComWork);
                    // log.Info(childURL);
                    HttpResponseMessage childrenvalues = client.GetAsync(childURL).Result;
                    childrenvalues.EnsureSuccessStatusCode();
                    dynamic WorkItemCS = await childrenvalues.Content.ReadAsAsync<object>();
                    var patchDocumentParent = getChildValuesandPatch(WorkItemCS, pathTotWork, pathRemWork, pathComWork, jsonTotWork, jsonRemWork, jsonComWork, fieldTotWork, fieldComWork, fieldRemWork );
                    if (patchDocumentParent != null)
                    {
                        var patchParentValue = new StringContent(JsonConvert.SerializeObject(patchDocumentParent), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call
                        var methodParentcall = new HttpMethod("PATCH");
                        var requestParent = new HttpRequestMessage(methodParentcall, orgname + workItemId + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchParentValue };
                        var responseSaveParent = client.SendAsync(requestParent).Result;
                    }

                }
                else
                {
                    if ((jObject["resource"]["revision"]["fields"]["System.WorkItemType"]).ToString() == "Task")
                    {
                        var patchDocument = ValidateandPatch(jObject, pathTotWork, pathRemWork, pathComWork, jsonTotWork, jsonRemWork, jsonComWork );
                        if (patchDocument != null)
                        {
                            var patchValue = new StringContent(JsonConvert.SerializeObject(patchDocument), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call

                            var methodcall = new HttpMethod("PATCH");
                            var request = new HttpRequestMessage(methodcall, orgname + workItemId.ToString() + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchValue };
                            var responseSave = client.SendAsync(request).Result;
                        }
                    }
                    else
                    {
                        Object[] patchDocumentNull = new Object[3];
                        patchDocumentNull[0] = new { op = "add", path = pathRemWork, value = "" };
                        patchDocumentNull[1] = new { op = "add", path = pathTotWork, value = "" };
                        patchDocumentNull[2] = new { op = "add", path = pathComWork, value = "" };
                        var patchParentValue = new StringContent(JsonConvert.SerializeObject(patchDocumentNull), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call
                        var methodParentcall = new HttpMethod("PATCH");
                        var requestParent = new HttpRequestMessage(methodParentcall, orgname + workItemId + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchParentValue };
                        var responseSaveParent = client.SendAsync(requestParent).Result;
                    }
                }
                log.Info("Service Hook Received for WorkItem: " + workItemId);
                // log.Info("Failed to parse the workItem id from the service hooks payload.");
            }
        }
        else if ((jObject["eventType"]).ToString() == "workitem.created")
        {
            if ((jObject["resource"]["fields"]["System.WorkItemType"]) != null && ((jObject["resource"]["fields"]["System.WorkItemType"]).ToString() == "Test Case" || (jObject["resource"]["fields"]["System.WorkItemType"]).ToString() == "Impediment"))
            {
                return req.CreateResponse(HttpStatusCode.OK);
            }
            else
            {
                workItemId = Int32.Parse((jObject["resource"]["id"]).ToString());
                ChildIds = GetChildItems(jObject);
                if (ChildIds.Count != 0)
                {
                    // log.Info ("No Parent Found");
                    if (jObject["resource"]["fields"][jsonTotWork] != null)
                    {
                        fieldTotWork = (jObject["resource"]["fields"][jsonTotWork]).ToString("0.##");
                    }
                    if (jObject["resource"]["fields"][jsonComWork] != null)
                    {
                        fieldComWork = (jObject["resource"]["fields"][jsonComWork]).ToString("0.##");
                    }
                    if (jObject["resource"]["fields"][jsonRemWork] != null)
                    {
                        fieldRemWork = (jObject["resource"]["fields"][jsonRemWork]).ToString("0.##");
                    }
                    string childURL = ChildInfURL(orgname, ChildIds, jsonTotWork, jsonRemWork, jsonComWork);
                    HttpResponseMessage childrenvalues = client.GetAsync(childURL).Result;
                    childrenvalues.EnsureSuccessStatusCode();
                    dynamic WorkItemCS = await childrenvalues.Content.ReadAsAsync<object>();
                    log.Info(WorkItemCS.ToString());                  
                    var patchDocumentParent = getChildValuesandPatch(WorkItemCS, pathTotWork, pathRemWork, pathComWork, jsonTotWork, jsonRemWork, jsonComWork, fieldTotWork, fieldComWork, fieldRemWork );
                    if (patchDocumentParent != null)
                    {
                        var patchParentValue = new StringContent(JsonConvert.SerializeObject(patchDocumentParent), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call
                        var methodParentcall = new HttpMethod("PATCH");
                        var requestParent = new HttpRequestMessage(methodParentcall, orgname + workItemId + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchParentValue };
                        var responseSaveParent = client.SendAsync(requestParent).Result;
                    }
                }
                else
                {
                    if ((jObject["resource"]["fields"]["System.WorkItemType"]).ToString() == "Task")
                    {
                        var patchDocument = ValidateandPatch(jObject, pathTotWork, pathRemWork, pathComWork, jsonTotWork, jsonRemWork, jsonComWork );
                        if (patchDocument != null)
                        {
                            var patchValue = new StringContent(JsonConvert.SerializeObject(patchDocument), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call

                            var methodcall = new HttpMethod("PATCH");
                            var request = new HttpRequestMessage(methodcall, orgname + workItemId.ToString() + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchValue };
                            var responseSave = client.SendAsync(request).Result;
                        }
                    }
                    else
                    {
                        Object[] patchDocumentNull = new Object[3];
                        patchDocumentNull[0] = new { op = "add", path = pathRemWork, value = "" };
                        patchDocumentNull[1] = new { op = "add", path = pathTotWork, value = "" };
                        patchDocumentNull[2] = new { op = "add", path = pathComWork, value = "" };
                        var patchParentValue = new StringContent(JsonConvert.SerializeObject(patchDocumentNull), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call
                        var methodParentcall = new HttpMethod("PATCH");
                        var requestParent = new HttpRequestMessage(methodParentcall, orgname + workItemId + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchParentValue };
                        var responseSaveParent = client.SendAsync(requestParent).Result;
                    }
                }
                log.Info("Service Hook Received for WorkItem: " + workItemId);
            }
        }
        else
        {
            return req.CreateResponse(HttpStatusCode.OK);
        }

        parentID = GetParentID(jObject);
        log.Info(parentID);
        //Getting Parent childs and updating Parent based on that. This happens recursively till top level parent is reached.
        while (parentID != "")
        {
             fieldTotWork = "";
             fieldComWork = "";
             fieldRemWork = "";
            HttpResponseMessage parentchildren = client.GetAsync(
                            orgname + parentID + "?$expand=all").Result;

            parentchildren.EnsureSuccessStatusCode();
            dynamic WorkItemPCS = await parentchildren.Content.ReadAsAsync<object>();
             log.Info(WorkItemPCS.ToString());
            if (WorkItemPCS["fields"][jsonTotWork] != null)
            {
                fieldTotWork = (WorkItemPCS["fields"][jsonTotWork]).ToString("0.##");
                // log.Info(fieldTotWork.ToString());
            }
            if (WorkItemPCS["fields"][jsonComWork] != null)
            {
                fieldComWork = (WorkItemPCS["fields"][jsonComWork]).ToString("0.##");
                //log.Info(fieldComWork.ToString());
            }
            if (WorkItemPCS["fields"][jsonRemWork] != null)
            {
                fieldRemWork = (WorkItemPCS["fields"][jsonRemWork]).ToString("0.##");
                //log.Info(fieldRemWork.ToString());
            }
            ChildIds = GetChildItems(WorkItemPCS);

            //Constructing URL for child Information:
            string childURL = ChildInfURL(orgname, ChildIds, jsonTotWork, jsonRemWork, jsonComWork);
            log.Info(childURL);
            HttpResponseMessage childrenvalues = client.GetAsync(
                        childURL).Result;

             
            childrenvalues.EnsureSuccessStatusCode();
            dynamic WorkItemCS = await childrenvalues.Content.ReadAsAsync<object>();
            log.Info(WorkItemCS.ToString()); 
            log.Info(fieldRemWork.ToString());                    
            var patchDocumentParent = getChildValuesandPatch(WorkItemCS, pathTotWork, pathRemWork, pathComWork, jsonTotWork, jsonRemWork, jsonComWork, fieldTotWork, fieldComWork, fieldRemWork );
            log.Info("I am here");
            if (patchDocumentParent != null)
            {
                log.Info("Inside Patching");
                var patchParentValue = new StringContent(JsonConvert.SerializeObject(patchDocumentParent), Encoding.UTF8, "application/json-patch+json"); // mediaType needs to be application/json-patch+json for a patch call

                var methodParentcall = new HttpMethod("PATCH");
                var requestParent = new HttpRequestMessage(methodParentcall, orgname + parentID + "?suppressNotifications=true&bypassRules=true&api-version=2.2") { Content = patchParentValue };
                var responseSaveParent = client.SendAsync(requestParent).Result;

            }
            parentID = GetParentID(WorkItemPCS);
        }
    }
    catch (Exception ex)
    {
        log.Info(ex.ToString());
        return req.CreateResponse(HttpStatusCode.OK);
    }
    return req.CreateResponse(HttpStatusCode.OK);
}

public static string GetParentID(dynamic jObject)
{
    string parentURL = "";
    string parentID = "";
    dynamic parentrel;
    dynamic parent;
    dynamic type;
    dynamic parentrelObject = null;
    if (jObject["relations"] != null)
    {
        parentrel = jObject["relations"].ToString();  // Invoked when parent is found through json reuqested by us through query
        parentrelObject = JsonConvert.DeserializeObject(parentrel.ToString());
    }
    else if (jObject["resource"] != null)  // Invoked when parent is found through server json (work item updated)
    { 
        if(jObject["resource"]["revision"] != null)
        {
            if (jObject["resource"]["revision"]["relations"] != null)
             {
            parentrel = jObject["resource"]["revision"]["relations"].ToString();
            parentrelObject = JsonConvert.DeserializeObject(parentrel.ToString());
             }
        }
       else // Invoked when parent is found through server json (work item created)
        { 
        if(jObject["resource"]["relations"] != null)
         {
        parentrel = jObject["resource"]["relations"].ToString();
        parentrelObject = JsonConvert.DeserializeObject(parentrel.ToString());
         }
        }
    }
    else
    {
        return "";
    }
    if (parentrelObject != null && (parentrelObject.Type).ToString() == "Array")
    {
        foreach (dynamic rel in parentrelObject)
        {
            parent = JsonConvert.DeserializeObject(rel.ToString());
            type = parent["rel"];
            if (type.ToString() == "System.LinkTypes.Hierarchy-Reverse")
            {
                parentURL = parent["url"];
                break;
            }
        }
    }
    if (parentURL != "")
    {
        string[] breakURL = parentURL.Split("/");
        parentID = breakURL[breakURL.Length - 1];
        return parentID;
    }
    return "";
}

public static List<string> GetChildItems(dynamic childs)
{
    List<string> ChildIds = new List<string>();
    dynamic parentChilds;
    dynamic childObject = null;
    dynamic parent;
    dynamic type;
    dynamic jObjectchilds = JsonConvert.DeserializeObject(childs.ToString());
    if (jObjectchilds["relations"] != null)  // Invoked when childs are found through json reuqested by us through query
    {
        parentChilds = jObjectchilds["relations"];
        childObject = JsonConvert.DeserializeObject(parentChilds.ToString());
    }
    else if (jObjectchilds["resource"] != null) // Invoked when childs are found through server json (work item updated) 
    {
        if(jObjectchilds["resource"]["revision"] != null)
        {
        if (jObjectchilds["resource"]["revision"]["relations"] != null)
        {
            parentChilds = jObjectchilds["resource"]["revision"]["relations"];
            childObject = JsonConvert.DeserializeObject(parentChilds.ToString());
        }
        }
       else  // Invoked when childs are found through server json (work item created) 
       {
        if(jObjectchilds["resource"]["relations"] != null)
        {
        parentChilds = jObjectchilds["resource"]["relations"];
        childObject = JsonConvert.DeserializeObject(parentChilds.ToString());
        }
       }
    }
    else
    {
        return ChildIds;
    }

    if (childObject != null && (childObject.Type).ToString() == "Array")
    {
        foreach (var child in childObject)
        {
            parent = JsonConvert.DeserializeObject(child.ToString());
            type = parent["rel"];
            if (type.ToString() == "System.LinkTypes.Hierarchy-Forward")
            {
                string childURL = parent["url"];
                string[] breakingURL = childURL.Split("/");
                string childID = breakingURL[breakingURL.Length - 1];
                ChildIds.Add(childID);
            }
        }
    }
    return ChildIds;

}

public static Object[] ValidateandPatch(dynamic jObject, string pathTotWork, string pathRemWork, string pathComWork, string jsonTotWork, string jsonRemWork, string jsonComWork )
{
    if ((jObject["eventType"]).ToString() == "workitem.created")
    {
        if (jObject["resource"]["fields"][jsonRemWork] == null && jObject["resource"]["fields"][jsonTotWork] != null)
        {

            string fieldTotWork = (jObject["resource"]["fields"][jsonTotWork]).ToString("0.##");
            string fieldComWork = (jObject["resource"]["fields"][jsonComWork]).ToString("0.##");
            if (jObject["resource"]["fields"]["System.State"] != null && (jObject["resource"]["fields"]["System.State"]).ToString() == "Done")
            {
                Object[] patchDocument = new Object[3];
                patchDocument[0] = new { op = "add", path = pathComWork, value = fieldTotWork };
                patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
                patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
                return patchDocument;
            }
            else
            {
                Object[] patchDocument = new Object[3];
                //decimal fieldRemWork = decimal.Parse(fieldTotWork) - decimal.Parse(fieldComWork);
                patchDocument[0] = new { op = "add", path = pathRemWork, value = fieldTotWork };
                patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
                patchDocument[2] = new { op = "add", path = pathComWork, value = "0" };
                return patchDocument;
            }
        }
        else if (jObject["resource"]["fields"][jsonRemWork] != null && jObject["resource"]["fields"][jsonTotWork] == null)
        {
            Object[] patchDocument = new Object[3];
            string fieldRemWork = (jObject["resource"]["fields"][jsonRemWork]).ToString("0.##");
            //string fieldComWork = (jObject["resource"]["fields"][jsonComWork]).ToString("0.##");
            //decimal fieldTotWork = decimal.Parse(fieldComWork) + decimal.Parse(fieldRemWork);
            patchDocument[0] = new { op = "add", path = pathTotWork, value = fieldRemWork };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = fieldRemWork };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "0" };
            return patchDocument;
        }

        else if (jObject["resource"]["fields"][jsonRemWork] != null && jObject["resource"]["fields"][jsonTotWork] != null)
        {
            Object[] patchDocument = new Object[3];
            string fieldTotWork = (jObject["resource"]["fields"][jsonTotWork]).ToString("0.##");
            string fieldRemWork = (jObject["resource"]["fields"][jsonRemWork]).ToString("0.##");
            if (jObject["resource"]["fields"]["System.State"] != null && (jObject["resource"]["fields"]["System.State"]).ToString() == "Done")
            {
                patchDocument[0] = new { op = "add", path = pathComWork, value = fieldTotWork };
                patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
                patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
                return patchDocument;
            }
            else
            {
            if (Decimal.Compare(decimal.Parse(fieldTotWork), decimal.Parse(fieldRemWork)) >= 0) //Checking if remaining is greater than total
            {
            decimal fieldComWork = decimal.Parse(fieldTotWork) - decimal.Parse(fieldRemWork);
            patchDocument[0] = new { op = "add", path = pathComWork, value = fieldComWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = fieldRemWork };
            }
            else
            {
            patchDocument[0] = new { op = "add", path = pathComWork, value = "0" };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldRemWork };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = fieldRemWork };
            }
            }
            return patchDocument;
        }
        else if (jObject["resource"]["fields"][jsonRemWork] == null && jObject["resource"]["fields"][jsonTotWork] == null && jObject["resource"]["fields"][jsonComWork] != null )
        {
            Object[] patchDocument = new Object[1];
            patchDocument[0] = new { op = "add", path = pathComWork, value = "" };
            return patchDocument;
        }
        else
        {
            return null;
        }

    }
    else
    {
        if (jObject["resource"]["revision"]["fields"][jsonRemWork] == null && jObject["resource"]["revision"]["fields"][jsonTotWork] != null)
        {
            string fieldTotWork = (jObject["resource"]["revision"]["fields"][jsonTotWork]).ToString("0.##");
           // string fieldComWork = (jObject["resource"]["revision"]["fields"][jsonComWork]).ToString("0.##");
            if (jObject["resource"]["revision"]["fields"]["System.State"] != null && (jObject["resource"]["revision"]["fields"]["System.State"]).ToString() == "Done")
            {
                Object[] patchDocument = new Object[3];
                patchDocument[0] = new { op = "add", path = pathComWork, value = fieldTotWork };
                patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
                patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
                return patchDocument;
            }
            else
            {
                Object[] patchDocument = new Object[3];
               // decimal fieldRemWork = decimal.Parse(fieldTotWork) - decimal.Parse(fieldComWork);
                patchDocument[0] = new { op = "add", path = pathRemWork, value = fieldTotWork };
                patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
                patchDocument[2] = new { op = "add", path = pathComWork, value = "0" };
                return patchDocument;
            }
        }

        else if (jObject["resource"]["revision"]["fields"][jsonRemWork] != null && jObject["resource"]["revision"]["fields"][jsonTotWork] == null)
        {
            Object[] patchDocument = new Object[3];
            string fieldRemWork = (jObject["resource"]["revision"]["fields"][jsonRemWork]).ToString("0.##");
            //string fieldComWork = (jObject["resource"]["revision"]["fields"][jsonComWork]).ToString("0.##");
            //decimal fieldTotWork = decimal.Parse(fieldComWork) + decimal.Parse(fieldRemWork);
            patchDocument[0] = new { op = "add", path = pathTotWork, value = fieldRemWork };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = fieldRemWork };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "0" };
            return patchDocument;
        }


        else if (jObject["resource"]["revision"]["fields"][jsonRemWork] != null && jObject["resource"]["revision"]["fields"][jsonTotWork] != null)
        {
            Object[] patchDocument = new Object[3];
            string fieldTotWork = (jObject["resource"]["revision"]["fields"][jsonTotWork]).ToString("0.##");
            string fieldRemWork = (jObject["resource"]["revision"]["fields"][jsonRemWork]).ToString("0.##");
            if (jObject["resource"]["revision"]["fields"]["System.State"] != null && (jObject["resource"]["revision"]["fields"]["System.State"]).ToString() == "Done")
            {
                patchDocument[0] = new { op = "add", path = pathComWork, value = fieldTotWork };
                patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
                patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
                return patchDocument;
            }
            else
            {
            if (Decimal.Compare(decimal.Parse(fieldTotWork), decimal.Parse(fieldRemWork)) >= 0)
            {
            decimal fieldComWork = decimal.Parse(fieldTotWork) - decimal.Parse(fieldRemWork);
            patchDocument[0] = new { op = "add", path = pathComWork, value = fieldComWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldTotWork };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = fieldRemWork };
            }
            else
            {
            patchDocument[0] = new { op = "add", path = pathComWork, value = "0" };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = fieldRemWork };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = fieldRemWork };
            }
            }
            return patchDocument;
        }
         else if (jObject["resource"]["revision"]["fields"][jsonRemWork] == null && jObject["resource"]["revision"]["fields"][jsonTotWork] == null && jObject["resource"]["revision"]["fields"][jsonComWork] != null )
        {
            Object[] patchDocument = new Object[1];
            patchDocument[0] = new { op = "add", path = pathComWork, value = "" };
            return patchDocument;
        }
        else
        {
            return null;
        }

    }

}

public static string ChildInfURL(string URL,List<string> ChildIds, string jsonTotWork, string jsonRemWork, string jsonComWork)
{
    //string URL = "https://dev.azure.com/raggarwal0541/_apis/wit/workitems/?ids=";
    string startURL = URL + "?ids=";
    foreach (string append in ChildIds)
    {
        if (append == ChildIds[ChildIds.Count - 1])
        {
            startURL = startURL + append;
        }
        else
        {
            startURL = startURL + append + ",";
        }
    }

    string EndURL = "&fields=System.State," + jsonRemWork + "," + jsonTotWork + "," + jsonComWork + "&api-version=5.0";

    return startURL + EndURL;

}

public static Object[] getChildValuesandPatch(dynamic WorkItemCS, string pathTotWork, string pathRemWork, string pathComWork, string jsonTotWork, string jsonRemWork, string jsonComWork, string fieldTotWork = "", string fieldComWork = "", string fieldRemWork = "" )
{

    List<string> RemainingWork = new List<string>();
    List<string> TotalWork = new List<string>();
    List<string> CompletedWork = new List<string>();
    decimal TotWork = 0;
    decimal Remwork = 0;
    decimal CompWork = 0;
    dynamic jObjectCS = JsonConvert.DeserializeObject(WorkItemCS.ToString());
    var Childsvalue = jObjectCS["value"];
    var childvalueObject = JsonConvert.DeserializeObject(Childsvalue.ToString());
    foreach (dynamic childvalue in childvalueObject)
    {
        dynamic childs = JsonConvert.DeserializeObject(childvalue.ToString());
        if (childs["fields"]["System.State"] == "Removed")
        {
            
        }
        else if (childs["fields"][jsonRemWork] == null && childs["fields"][jsonTotWork] == null && childs["fields"][jsonComWork] == null)
        {

        }
        else if (childs["fields"][jsonRemWork] != null && childs["fields"][jsonTotWork] == null && childs["fields"][jsonComWork] == null)
        {
            RemainingWork.Add((childs["fields"][jsonRemWork]).ToString("0.##"));
        }
        else if (childs["fields"][jsonRemWork] == null && childs["fields"][jsonTotWork] != null && childs["fields"][jsonComWork] == null)
        {
            TotalWork.Add((childs["fields"][jsonTotWork]).ToString("0.##"));
        }
        else if (childs["fields"][jsonRemWork] == null && childs["fields"][jsonTotWork] == null && childs["fields"][jsonComWork] != null)
        {
            CompletedWork.Add((childs["fields"][jsonComWork]).ToString("0.##"));
        }
        else if (childs["fields"][jsonRemWork] != null && childs["fields"][jsonTotWork] != null && childs["fields"][jsonComWork] == null)
        {
            TotalWork.Add((childs["fields"][jsonTotWork]).ToString("0.##"));
            RemainingWork.Add((childs["fields"][jsonRemWork]).ToString("0.##"));
        }
        else if (childs["fields"][jsonRemWork] != null && childs["fields"][jsonTotWork] == null && childs["fields"][jsonComWork] != null)
        {
            RemainingWork.Add((childs["fields"][jsonRemWork]).ToString("0.##"));
            CompletedWork.Add((childs["fields"][jsonComWork]).ToString("0.##"));
        }
        else if (childs["fields"][jsonRemWork] == null && childs["fields"][jsonTotWork] != null && childs["fields"][jsonComWork] != null)
        {
            TotalWork.Add((childs["fields"][jsonTotWork]).ToString("0.##"));
            CompletedWork.Add((childs["fields"][jsonComWork]).ToString("0.##"));
        }
        else
        {
            RemainingWork.Add((childs["fields"][jsonRemWork]).ToString("0.##"));
            TotalWork.Add((childs["fields"][jsonTotWork]).ToString("0.##"));
            CompletedWork.Add((childs["fields"][jsonComWork]).ToString("0.##"));
        }
    }

    if (TotalWork.Count != 0)
    {
        foreach (string total in TotalWork)
        {
            TotWork = TotWork + decimal.Parse(total);
            //log.Info(finalwork.ToString());
        }
    }
    if (RemainingWork.Count != 0)
    {
        foreach (string Remaining in RemainingWork)
        {
            Remwork = Remwork + decimal.Parse(Remaining);
            //log.Info(Remainingwork.ToString());
        }
    }
    if (CompletedWork.Count != 0)
    {
        foreach (string Completed in CompletedWork)
        {
            CompWork = CompWork + decimal.Parse(Completed);
            //log.Info(Remainingwork.ToString());
        }
    }
    if (TotalWork.Count == 0 && RemainingWork.Count != 0 && CompletedWork.Count != 0)
    {
        if (string.Equals(fieldComWork, "") || string.Equals(fieldRemWork, "") || !string.Equals(fieldTotWork, "") )
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathTotWork, value = "" };

            return patchDocument;
        }
        else if (decimal.Parse(fieldComWork) != CompWork || decimal.Parse(fieldRemWork) != Remwork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathTotWork, value = "" };

            return patchDocument;
        }
        else
        {
            return null;
        }
    }
    else if (TotalWork.Count != 0 && RemainingWork.Count == 0 && CompletedWork.Count != 0)
    {
        if (string.Equals(fieldComWork, "") || string.Equals(fieldTotWork, "") || !string.Equals(fieldRemWork, ""))
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };

            return patchDocument;
        }
        else if (decimal.Parse(fieldComWork) != CompWork || decimal.Parse(fieldTotWork) != TotWork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
            return patchDocument;
        }
        else
        {
            return null;
        }
    }
    else if (TotalWork.Count != 0 && RemainingWork.Count != 0 && CompletedWork.Count == 0)
    {
        if (string.Equals(fieldRemWork, "") || string.Equals(fieldTotWork, "") || !string.Equals(fieldComWork, ""))
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

            return patchDocument;
        }
        else if (decimal.Parse(fieldRemWork) != Remwork || decimal.Parse(fieldTotWork) != TotWork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

            return patchDocument;
        }
        else
        {
            return null;
        }
    }
    else if (TotalWork.Count == 0 && RemainingWork.Count != 0 && CompletedWork.Count == 0)
    {
        if (string.Equals(fieldRemWork, "") || !string.Equals(fieldTotWork, "") || !string.Equals(fieldComWork, ""))
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = "" };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

            return patchDocument;

        }
        else if (decimal.Parse(fieldRemWork) != Remwork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = "" };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

            return patchDocument;
        }

        else
        {
            return null;
        }
    }
    else if (TotalWork.Count != 0 && RemainingWork.Count == 0 && CompletedWork.Count == 0)
    {
        if (string.Equals(fieldTotWork, "") || !string.Equals(fieldRemWork, "") || !string.Equals(fieldComWork, ""))
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = "" };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

            return patchDocument;

        }
        else if (decimal.Parse(fieldTotWork) != TotWork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = "" };
            patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

            return patchDocument;
        }

        else
        {
            return null;
        }
    }
    else if (TotalWork.Count == 0 && RemainingWork.Count == 0 && CompletedWork.Count != 0)
    {
        if (string.Equals(fieldComWork, "") || !string.Equals(fieldRemWork, "") || !string.Equals(fieldTotWork, ""))
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = "" };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
            return patchDocument;

        }
        else if (decimal.Parse(fieldComWork) != CompWork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathTotWork, value = "" };
            patchDocument[2] = new { op = "add", path = pathRemWork, value = "" };
            return patchDocument;
        }

        else
        {
            return null;
        }
    }
    else if (TotalWork.Count != 0 && RemainingWork.Count != 0 && CompletedWork.Count != 0)
    {
        if (string.Equals(fieldComWork, "") || string.Equals(fieldTotWork, "") || string.Equals(fieldRemWork, ""))
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };

            return patchDocument;

        }
        else if (decimal.Parse(fieldTotWork) != TotWork || decimal.Parse(fieldComWork) != CompWork || decimal.Parse(fieldRemWork) != Remwork)
        {
            Object[] patchDocument = new Object[3];
            patchDocument[0] = new { op = "add", path = pathTotWork, value = TotWork.ToString("0.##") };
            patchDocument[1] = new { op = "add", path = pathRemWork, value = Remwork.ToString("0.##") };
            patchDocument[2] = new { op = "add", path = pathComWork, value = CompWork.ToString("0.##") };

            return patchDocument;
        }

        else
        {
            return null;
        }
    }
    else
    {
        Object[] patchDocument = new Object[3];
        patchDocument[0] = new { op = "add", path = pathTotWork, value = "" };
        patchDocument[1] = new { op = "add", path = pathRemWork, value = "" };
        patchDocument[2] = new { op = "add", path = pathComWork, value = "" };

        return patchDocument;
    }
}