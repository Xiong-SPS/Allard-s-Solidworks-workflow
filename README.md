# SPS SolidWorks workflow

Reusable modeling instructions, planning tools, native SolidWorks helpers,
and verification tools. Start with [AGENTS.md](AGENTS.md), then the
[workflow guide](sps-workflow/README.md).

This repository is configured to include the workflow and instructions only.
Customer job folders, production CAD files, temporary files, backups and
generated native test outputs are excluded. The existing local files remain
in place. The library numbering and PDM search notes are reference evidence;
use the authoritative workbook and current library for actual decisions.

## Use on another PC or account

1. Sign in to a GitHub account with access to the private repository
   [SpsGuy/Sps-solidworks-workflow.](https://github.com/SpsGuy/Sps-solidworks-workflow.).
2. Give the other GitHub account access to the private repository. A different
   Codex account can use the cloned files after authenticating to GitHub with an
   account that has repository access.
3. On the other Windows PC, install Git, a supported Python 3 runtime, and a
   licensed SolidWorks installation with its interop assemblies. This code uses
   modern Python syntax; Python 3.12 is the version used on the original PC.
   Native scripts run through Windows PowerShell 5.1/.NET Framework.
4. Clone the repository into a normal local working folder and open that
   folder as a project in Codex. Read its AGENTS.md before modeling.
5. Configure access to the company's PDM vault, shared drive, approved document
   templates, material databases, library models and standard blocks. Sign in
   separately to any required services on that PC.
6. Run the offline checks below, then compile the native helpers. Before actual
   modeling, reconcile the local library paths and validate the selected native
   configurations against current source evidence.

```powershell
git clone https://github.com/SpsGuy/Sps-solidworks-workflow..git sps-solidworks-workflow
cd sps-solidworks-workflow
python -m unittest discover -s .\sps-workflow\tests -v
.\sps-workflow\Invoke-Native.ps1 -CompileOnly
```

For a different SolidWorks installation directory:

```powershell
.\sps-workflow\Invoke-Native.ps1 -CompileOnly -SolidWorksInteropDirectory 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS'
```

The default remains the original PC's `SOLIDWORKS (2)` installation directory.
The override directory must contain both `SolidWorks.Interop.sldworks.dll` and
`SolidWorks.Interop.swconst.dll`.

## Dependencies outside Git

Git transfers the checked-in files. It does not transfer installed applications,
licenses, Codex chat history, account settings, plugins, credentials, PDM access,
or external library content. Install required tools and configure each account
separately. Follow [modeling rules](sps-workflow/modeling-rules.md) on every PC;
the same workflow still requires current native assembly and independent
engineering reviews.

The current workflow refers to these external locations:

| Dependency | Original location / action |
| --- | --- |
| Library models and rolled-pipe templates | `C:\SPS\Libraries\SolidWorks Fittings`; configure the approved vault/library on the other PC. |
| Part and assembly templates used by native demonstrations | `C:\SPS\Admin\SW Shared\Document Templates`; inspect the demonstration sources before running them. |
| Standard half-coupling block | `H:\Engineering documents\Solidworks\Blocks\34 HALF-COUPLING.SLDBLK`; configure the shared drive or deliberately adapt the demonstration scripts. |
| Material numbering workbook | `H:\Engineering documents\MATERIAL PART NUMBERING UPDATE ver2.xlsx`; obtain current authorized access. |
| Library shortlist | `sps-workflow/index/library-index.json` records original absolute paths and hashes. Missing paths are uncached; do not treat them as verified local files. |

Historical job links and some native demonstrations depend on job evidence
excluded from this repository. In particular, `workflow.py build-index` imports
the original job's audit files, and native demonstrations use original job
fixtures. A workflow-only clone does not contain those fixtures. Do not invent
replacement evidence, rewrite historical hashes to make checks pass, or run the
demonstrations until their dependencies have been deliberately supplied and
reviewed. Most offline unit tests use synthetic temporary fixtures. The seed
index provenance test additionally requires the real local library and complete
measurement provenance. During repository preparation, 39 of 40 tests passed;
that test failed because an existing index entry lacks its `provenance` field.
This is an unresolved source-index issue, and the clone is not certified ready
for production modeling. Restore provenance from reviewed native evidence
before relying on the affected shortlist entry; do not weaken the test.

New customer jobs should retain their own checked plan, evidence, decisions,
exceptions and JOB-STATE.md in the authorized job location. The root gitignore
excludes all new top-level job folders by default. Review scope deliberately
before changing that rule. Job 1016670-147883 remains canceled.

## Synchronize

After authenticating Git on the PC, review and publish intended workflow edits:

```powershell
git add .
git diff --cached --stat
git commit -m "Describe the workflow change"
git push
```

Set your own Git commit name/email first if Git requests them. Never put access
tokens or passwords in repository files or remote URLs. The repository's current
name ends in a period, so its HTTPS Git URL ends in `workflow..git`. The clone
command above supplies a folder name without that trailing period for Windows.

On either PC, use `git pull --ff-only` before making changes. Review and commit
intended workflow changes, then `git push` so the other PC can pull them.
Coordinate edits between PCs and resolve any conflicts before continuing.
