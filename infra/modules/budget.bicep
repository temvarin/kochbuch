// A cost alarm on the resource group: mail when the month's actual cost passes half of the budget and when it
// passes all of it. The design targets well under $1 a month (docs/adr/0011); this catches a mistake early.

param amount int
param startDate string

@description('Who gets the alarm mail.')
param contactEmails array

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-kochbuch'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate }
    notifications: {
      half: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: contactEmails
      }
      all: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: contactEmails
      }
    }
  }
}
