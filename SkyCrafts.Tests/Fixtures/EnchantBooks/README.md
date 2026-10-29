# Enchantment book fixtures

These 13 item JSON extracts come from [NotEnoughUpdates-REPO at `85b1dd647bf5ad73385c3cf11d15b8c29bad208e`](https://github.com/NotEnoughUpdates/NotEnoughUpdates-REPO/tree/85b1dd647bf5ad73385c3cf11d15b8c29bad208e/items), downloaded on 2026-10-29:

- `LUCK;1.json` through `LUCK;5.json`
- `ULTIMATE_WISE;1.json` through `ULTIMATE_WISE;5.json`
- `FIRE_ASPECT;1.json` through `FIRE_ASPECT;3.json`

Each filename matches its upstream `items/` filename. The extracts retain only `itemid`, `displayname`, `internalname`, and `lore`; all retained values are unchanged. JSON whitespace and field order were normalized. The lore preserves the current `§6Sources:` heading and bullet format, including ranges and single levels.

The test project copies these files to its output directory. Tests load every expected file directly, so missing fixtures fail the tests. They do not depend on an external checkout, network access, or the current upstream branch. Existing synthetic tests cover the legacy source format.

## Upstream license

Copied from [the pinned upstream LICENSE](https://github.com/NotEnoughUpdates/NotEnoughUpdates-REPO/blob/85b1dd647bf5ad73385c3cf11d15b8c29bad208e/LICENSE):

```text
MIT License

Copyright (c) 2020 Moulberry

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
