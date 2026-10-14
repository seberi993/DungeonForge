-- Apply once to the Supabase project (SQL editor or migration). Accessed only with the service key from the API.
create table if not exists public.run_scores (
    id          uuid primary key,
    party_name  text        not null,
    heroes      text[]      not null,
    floor       int         not null,
    turns       int         not null,
    gold        int         not null,
    achieved_at timestamptz not null
);

create index if not exists run_scores_rank on public.run_scores (floor desc, gold desc, turns desc, achieved_at asc);

-- No policies: the service role bypasses RLS, anon/authenticated get nothing.
alter table public.run_scores enable row level security;
