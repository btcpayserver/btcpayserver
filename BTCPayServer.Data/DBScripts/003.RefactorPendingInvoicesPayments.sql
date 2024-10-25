CREATE OR REPLACE FUNCTION is_pending(status TEXT)
RETURNS BOOLEAN AS $$
    SELECT status = 'Processing' OR status = 'New';
$$ LANGUAGE sql IMMUTABLE;

CREATE INDEX "IX_Invoices_Pending" ON "Invoices"((1)) WHERE is_pending("Status");
CREATE INDEX "IX_Payments_Pending" ON "Payments"((1)) WHERE is_pending("Status");
DROP TABLE "PendingInvoices";
ANALYZE "Invoices";
