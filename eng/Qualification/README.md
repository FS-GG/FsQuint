# F# qualification commands

Run from the repository root with SDK 10.0.401:

```sh
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- selftest
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- replay
```

`replay` restores a locked external consumer, checks package and source identities,
then runs the Automata conformance examples. With `QUINT_BIN` and `QUINT_HOME` set, it
also runs `generate` and the pinned model checks. `generate --write` adds reviewed new
fixtures; it refuses changed semantics for an existing regression fixture.

`providers-provision DEST` builds pinned PostgreSQL and extensions in a new private
destination. `providers-check SOURCE POSTGRES_BIN` runs the provider contracts and
crash/restart witnesses. The [provider guide](../../examples/AutomataProviders/README.md)
has the prerequisite and capability details.

`readback VERSION [COMMIT]` checks published FsQuint packages against the release
source tag and qualified archive payloads. The release workflow supplies credentials
for the GitHub package feed. `selftest` exercises the same F# archive and nuspec
readers against malformed members, identities and source tags without contacting a
package feed. The release workflow runs it before readback.

The project uses only the repository's .NET SDK and pinned FSharp.Core. Shell scripts
remain for tool download, packing and CI entry points; FsQuint qualification and release
do not need a Python runtime. The separately installed work-roadmap skill retains its
own Python helpers.
