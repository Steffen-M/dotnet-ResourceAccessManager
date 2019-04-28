# dotnet-ResourceAccessManager

Manages exclusive access to named resources in a TPL friendly manner.

## Notes

1. `AquireExclusiveAccessAsync` should always be called with a timeout, or an `CancellationToken`
having a timeout set.

2. Nested calls to `AquireExclusiveAccessAsync` for the same resource are not supported by design.

## Usage example

```c#
static async Task Example1(string fileName)
{
    using (await ResourceAccessManager.Default.AquireExclusiveAccessAsync(fileName, TimeSpan.FromSeconds(5)))
    {
        // do something with the file, 
        // you've got exclusive access to it (within your application)
    }
}
```
