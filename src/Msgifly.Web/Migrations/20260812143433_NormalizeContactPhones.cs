using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeContactPhones : Migration
    {
        /// <summary>
        /// Data-only fix, no schema change: before PhoneNumberNormalizer existed, Contact.Phone
        /// got written straight from whatever format its source handed over — raw digits from
        /// the WhatsApp webhook, "+91 82086 78144"-style from a Lead Ads form, anything a human
        /// typed into the manual Add Contact form. Two different-looking rows for the same real
        /// person (e.g. "918208678144" and "+91 82086 78144") is exactly what that produced —
        /// dedup checks compare exact strings, so they never recognized each other. This
        /// normalizes every already-stored number to the same digits-only form the app now
        /// always writes, so future dedup actually works against old data too. Doesn't touch
        /// rows that are already clean.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Contacts
                SET Phone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    Phone, ' ', ''), '+', ''), '-', ''), '(', ''), ')', ''), '.', ''), CHAR(9), '')
                WHERE Phone LIKE '%[^0-9]%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op — stripping formatting is lossy (we don't know which rows
            // had a "+" vs spaces vs nothing to begin with), so there's nothing meaningful to
            // restore. Rolling back just leaves the now-normalized values in place.
        }
    }
}
