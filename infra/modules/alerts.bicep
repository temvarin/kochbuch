// The few things that should reach a person (docs/adr/0009, Newman ch10 p.329-333): each alert means "look now",
// names what broke, and says where to look. Metric alerts only: $0.10 per watched series a month, where a log-query
// alert would cost $0.50 or more each. Everything else is a saved query to run when wondering (infra/queries).

param namePrefix string

@description('Application Insights, for failed requests and failed function runs.')
param insightsId string

@description('The Service Bus namespace, for dead-lettered messages.')
param serviceBusNamespaceId string

@description('The Event Grid system topic of the photo storage, for uploads it could not deliver.')
param uploadTopicId string

@description('Who gets the alert mails: the same people as the budget alarm.')
param emails array

resource mail 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${namePrefix}'
  location: 'global'
  properties: {
    groupShortName: take(namePrefix, 12)
    enabled: true
    emailReceivers: [for (email, i) in emails: {
      name: 'mail-${i}'
      emailAddress: email
      useCommonAlertSchema: true
    }]
  }
}

var alerts = [
  {
    name: 'failed-requests'
    scope: insightsId
    severity: 2
    // A phone got a 5xx, or a function run threw: either way someone saw an error or a photo wasn't done.
    description: 'Requests failed: a 5xx answer from the gateway or a service, or a failed run of the Media function. Open infra/queries/failures.kql in the workspace (saved as "kochbuch: failures"); its Example column is an operation id for trace.kql.'
    window: 'PT15M'
    criterion: {
      criterionType: 'StaticThresholdCriterion'
      name: 'failed'
      metricNamespace: 'microsoft.insights/components'
      metricName: 'requests/failed'
      timeAggregation: 'Count'
      operator: 'GreaterThan'
      threshold: 0
    }
  }
  {
    name: 'dead-letters'
    scope: serviceBusNamespaceId
    severity: 2
    // Ten failed deliveries in a row: a photo that isn't processed or a clean-up that isn't applied, until someone acts.
    description: 'A message was dead-lettered in a Service Bus queue (the alert names which). photo-uploads: an upload Media could not process; photo-lifecycle: a photo event it could not apply. Read the dead-letter reason in the portal (Service Bus Explorer), fix, then resubmit or delete the message (docs/adr/0006, 0008).'
    window: 'PT1H'
    criterion: {
      criterionType: 'StaticThresholdCriterion'
      name: 'deadlettered'
      metricNamespace: 'Microsoft.ServiceBus/namespaces'
      metricName: 'DeadletteredMessages'
      timeAggregation: 'Maximum'
      operator: 'GreaterThan'
      threshold: 0
      // One series per queue, so the mail says which queue.
      dimensions: [{ name: 'EntityName', operator: 'Include', values: ['*'] }]
    }
  }
  {
    name: 'undelivered-uploads'
    scope: uploadTopicId
    severity: 2
    description: 'Event Grid could not hand an upload to the photo-uploads queue and wrote it to the deadletter container of the photo storage: that photo was never processed. Check the queue and the topic\'s role assignment, then process the upload again (tools/Koch.DevPhotos, docs/adr/0006).'
    window: 'PT1H'
    criterion: {
      criterionType: 'StaticThresholdCriterion'
      name: 'deadlettered'
      metricNamespace: 'Microsoft.EventGrid/systemTopics'
      metricName: 'DeadLetteredCount'
      timeAggregation: 'Total'
      operator: 'GreaterThan'
      threshold: 0
    }
  }
]

resource metricAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [for alert in alerts: {
  name: 'alert-${namePrefix}-${alert.name}'
  location: 'global'
  properties: {
    description: alert.description
    severity: alert.severity
    enabled: true
    scopes: [alert.scope]
    evaluationFrequency: 'PT5M'
    windowSize: alert.window
    // Resolves itself, with a second mail, once the condition is gone.
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [alert.criterion]
    }
    actions: [{ actionGroupId: mail.id }]
  }
}]
