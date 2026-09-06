<!--
  What changed and why. If it fixes a defect, say what the defect was — the next person reading
  the history usually wants that more than the diff.
-->

## Verification

<!--
  How this was checked. Tests are the default; for anything touching input handling, say which
  hostile file was constructed and what it did before and after.
-->

- [ ] `dotnet build -warnaserror` clean
- [ ] `dotnet test` passes on net8.0 and net10.0
- [ ] `python tools/reference-diff.py` still agrees with the reference validator
