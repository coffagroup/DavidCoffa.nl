CREATE TABLE IF NOT EXISTS contact_leads (
  id uuid PRIMARY KEY,
  name varchar(120) NOT NULL,
  email varchar(160) NOT NULL,
  subject varchar(160),
  message text NOT NULL CHECK (char_length(message) BETWEEN 10 AND 4000),
  status varchar(20) NOT NULL DEFAULT 'new' CHECK (status IN ('new', 'contacted', 'closed')),
  created_at timestamptz NOT NULL DEFAULT now(),
  source varchar(40) NOT NULL DEFAULT 'website'
);

CREATE INDEX IF NOT EXISTS contact_leads_status_created_at_idx ON contact_leads (status, created_at DESC);
