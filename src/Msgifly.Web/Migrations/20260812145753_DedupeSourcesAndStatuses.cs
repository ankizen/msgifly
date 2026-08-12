using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class DedupeSourcesAndStatuses : Migration
    {
        /// <summary>
        /// Data-only fix, no schema change: the Workspace migration's original seeding order
        /// checked "does this workspace already have Sources/Statuses" before running the
        /// backfill that would have found the legacy ones, so it always seeded a second fresh
        /// set on top (see DbSeeder.EnsureDefaultWorkspaceAsync). Every Source/Status ended up
        /// duplicated per workspace. This keeps the lowest-id row per (WorkspaceId, Name),
        /// repoints any Contact/Workspace referencing a duplicate onto the kept row, then
        /// deletes the duplicates.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID('tempdb..#SourceMap') IS NOT NULL DROP TABLE #SourceMap;
                SELECT s.Id AS DupId, m.KeepId
                INTO #SourceMap
                FROM Sources s
                JOIN (SELECT WorkspaceId, Name, MIN(Id) AS KeepId FROM Sources GROUP BY WorkspaceId, Name) m
                    ON s.WorkspaceId = m.WorkspaceId AND s.Name = m.Name
                WHERE s.Id <> m.KeepId;

                UPDATE c SET c.SourceId = sm.KeepId
                FROM Contacts c JOIN #SourceMap sm ON c.SourceId = sm.DupId;

                UPDATE w SET w.DefaultLeadSourceId = sm.KeepId
                FROM Workspaces w JOIN #SourceMap sm ON w.DefaultLeadSourceId = sm.DupId;

                DELETE s FROM Sources s JOIN #SourceMap sm ON s.Id = sm.DupId;

                IF OBJECT_ID('tempdb..#StatusMap') IS NOT NULL DROP TABLE #StatusMap;
                SELECT s.Id AS DupId, m.KeepId
                INTO #StatusMap
                FROM Statuses s
                JOIN (SELECT WorkspaceId, Name, MIN(Id) AS KeepId FROM Statuses GROUP BY WorkspaceId, Name) m
                    ON s.WorkspaceId = m.WorkspaceId AND s.Name = m.Name
                WHERE s.Id <> m.KeepId;

                UPDATE c SET c.StatusId = sm.KeepId
                FROM Contacts c JOIN #StatusMap sm ON c.StatusId = sm.DupId;

                UPDATE w SET w.DefaultLeadStatusId = sm.KeepId
                FROM Workspaces w JOIN #StatusMap sm ON w.DefaultLeadStatusId = sm.DupId;

                DELETE s FROM Statuses s JOIN #StatusMap sm ON s.Id = sm.DupId;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op — merging duplicates is lossy (which row was the "original"
            // isn't recoverable), so there's nothing meaningful to restore.
        }
    }
}
