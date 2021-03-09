# Customer Insights Log Processor

A basic Azure function that reads Customer Insights Diagnostics Settings events from Event Hubs and sends them to a Log Analytics Workspace via [data collector api](https://docs.microsoft.com/en-us/azure/azure-monitor/logs/data-collector-api) 

Customer Insights creates 2 Event Hubs named `insight-log-audit` and `insight-log-operational` these are the 2 categories of events created by the platform. To send them both to Log Analytics you will need to setup 2 azure functions each pointing to one Event Hub.

## Application Settings
* **WORKSPACEID** Log Analytics workspace id key.
* **SHAREDKEY** Use either the primary or secondary Log Analytics key.
* **LOG_NAME** Name of the table that will be created in Log Analytics if it doesn't already exist or where data will be appended, you will find the table in the *Custom Logs* collection with an '_CL' sufix added to the log name.
* **PROPSTOEXCLUDE** A coma separated string value of json properties to remove from each event, can be use to remove the identity property to avoid storing personally identifiable information data. 

