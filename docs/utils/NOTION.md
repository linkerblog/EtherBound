# EtherBound: Notion

[Guide] [Process]

How work is reflected in the EtherBound Notion page. Read it when you write a report or a Dev Blog
entry.

## 1. Systems Index

Every system update must also be reflected in the EtherBound Notion page
(`EtherBound — Systems Index`), preserving the existing page format, table structure,
separator before the subindexes, and corresponding icon.

## 2. Dev Blog

The `Dev Blog` subindex is a database with one entry per `Dev-XYZ`, `FixNN` or `InfraNN` that
changes code, created when the doc moves to `docs/done/`; later work on the same doc updates its
entry instead of adding a new one. Entries use a technical, descriptive title and an icon that
matches the topic, and fill Kind, Plan, Phase and Modules; Version is filled once the release
commit exists.

## 3. Work Reports

When a task is completed, add a concise report under its own `Work Reports` subindex; reports must
not be mixed into another subindex. Changes to docs, rules or workflow, and small edits without a
plan, go only in the Work Report. Its entry is the commit's tags and TL;DR under its `HH:MM`
heading.

Work report entries must be pages under the `Work Reports` subindex, titled `Report DD/MM/YYYY`,
with an icon that matches the report's topic. Work Reports are consolidated by date: before
creating a report, search for that date and update the existing page. There must be only one Work
Report page per date, containing all completed work for that day. Inside the daily page, separate
updates by modification time using `HH:MM` headings, ordered chronologically.
