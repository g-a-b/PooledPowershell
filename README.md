# PooledPowershell
A .Net library to simplify and manage the execution of concurrent Powershell scripts

## Status
This is a working but not deeply enough tested library. It may be very buggy.
Any comment or suggestion are welcomed.

## Example

### C# #
```csharp
String[] servers = { "SRV1", "SRV2", "SVR3" }

PooledPowershell pooledPowershell = new PooledPowershell.BasicPooledPowershell(1, 20)

foreach (server in servers) {
	pooledPowershell.CreatePooledJob(server).AddCommand("Get-Service").AddParameter("ComputerName", server) | Out-Null
}

pooledPowershell.Invoke()

foreach (PooledJob job in pooledPowershell.Jobs) {
	if (job.InvocationStateInfo.State == System.Management.Automation.PSInvocationState.Completed) {
		// Do something
	} else {
		// Something went wrong
	}
}
```

### Powershell
```powershell
Add-Type -Path .\PooledPowershell.dll

$servers = Get-ExchangeServer

$pooledPowershell = New-Object PooledPowershell.Ex2007PooledPowershell(1, 20, $Host)

foreach ($server in $servers) {
	$pooledPowershell.CreatePooledJob($server).AddScript("Test-ServiceHealth -Server $server | Select-Object Role,RequiredServicesRunning") | Out-Null
}

$pooledPowershell.BeginInvoke()

$pooledPowershell.EndInvoke(10000)

foreach ($job in $pooledPowershell.Jobs) {
	if ($job.InvocationStateInfo.State -eq "Completed") {
		# Do something
	} else {
		# Timeout?
	}
}
```