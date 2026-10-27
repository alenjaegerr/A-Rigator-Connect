-- A-Rigator Connect Supabase schema
-- This file is intended to be run in the Supabase SQL editor.

create extension if not exists pgcrypto;

-- =============================
-- Profiles
-- =============================
create table if not exists public.profiles (
	id uuid primary key references auth.users(id) on delete cascade,
	email text not null,
	display_name text not null default '',
	avatar_url text,
	created_at timestamptz not null default now(),
	updated_at timestamptz not null default now()
);

create index if not exists idx_profiles_email on public.profiles(email);

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
	insert into public.profiles (id, email, display_name)
	values (
		new.id,
		new.email,
		coalesce(new.raw_user_meta_data->>'full_name', split_part(new.email, '@', 1))
	)
	on conflict (id) do nothing;

	return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
	after insert on auth.users
	for each row execute procedure public.handle_new_user();

-- =============================
-- Robots
-- =============================
create table if not exists public.robots (
	id uuid primary key default gen_random_uuid(),
	owner_id uuid not null references public.profiles(id) on delete cascade,
	name text not null,
	robot_code text unique,
	connection_mode text not null default 'local' check (connection_mode in ('local', 'remote')),
	local_ap_ssid text,
	remote_mode_enabled boolean not null default false,
	last_seen_at timestamptz,
	created_at timestamptz not null default now(),
	updated_at timestamptz not null default now()
);

create index if not exists idx_robots_owner_id on public.robots(owner_id);
create index if not exists idx_robots_robot_code on public.robots(robot_code);
create index if not exists idx_robots_connection_mode on public.robots(connection_mode);

-- =============================
-- Robot connection history
-- =============================
create table if not exists public.robot_connections (
	id uuid primary key default gen_random_uuid(),
	robot_id uuid not null references public.robots(id) on delete cascade,
	mode text not null check (mode in ('local', 'remote')),
	network_ssid text,
	remote_endpoint text,
	status text not null default 'pending' check (status in ('pending', 'connected', 'disconnected', 'failed')),
	connected_at timestamptz,
	disconnected_at timestamptz,
	created_at timestamptz not null default now()
);

create index if not exists idx_robot_connections_robot_id on public.robot_connections(robot_id);
create index if not exists idx_robot_connections_status on public.robot_connections(status);

-- =============================
-- Robot sync activity / events
-- =============================
create table if not exists public.robot_sync_events (
	id bigserial primary key,
	robot_id uuid not null references public.robots(id) on delete cascade,
	event_type text not null,
	payload jsonb not null default '{}'::jsonb,
	created_at timestamptz not null default now()
);

create index if not exists idx_robot_sync_events_robot_id on public.robot_sync_events(robot_id);
create index if not exists idx_robot_sync_events_created_at on public.robot_sync_events(created_at desc);

-- =============================
-- Helpful updated_at trigger
-- =============================
create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
	new.updated_at = now();
	return new;
end;
$$;

drop trigger if exists set_updated_at_on_profiles on public.profiles;
create trigger set_updated_at_on_profiles
	before update on public.profiles
	for each row execute procedure public.set_updated_at();

drop trigger if exists set_updated_at_on_robots on public.robots;
create trigger set_updated_at_on_robots
	before update on public.robots
	for each row execute procedure public.set_updated_at();

-- =============================
-- Row level security
-- =============================
alter table public.profiles enable row level security;
alter table public.robots enable row level security;
alter table public.robot_connections enable row level security;
alter table public.robot_sync_events enable row level security;

-- profiles policies
create policy "Profiles are viewable by owner"
on public.profiles
for select
using (auth.uid() = id);

create policy "Profiles can be inserted by owner"
on public.profiles
for insert
with check (auth.uid() = id);

create policy "Profiles can be updated by owner"
on public.profiles
for update
using (auth.uid() = id)
with check (auth.uid() = id);

-- robots policies
create policy "Robots are viewable by owner"
on public.robots
for select
using (auth.uid() = owner_id);

create policy "Robots can be inserted by owner"
on public.robots
for insert
with check (auth.uid() = owner_id);

create policy "Robots can be updated by owner"
on public.robots
for update
using (auth.uid() = owner_id)
with check (auth.uid() = owner_id);

create policy "Robots can be deleted by owner"
on public.robots
for delete
using (auth.uid() = owner_id);

-- robot_connections policies
create policy "Robot connections are viewable by robot owner"
on public.robot_connections
for select
using (
	exists (
		select 1
		from public.robots r
		where r.id = robot_connections.robot_id
		  and r.owner_id = auth.uid()
	)
);

create policy "Robot connections can be inserted by robot owner"
on public.robot_connections
for insert
with check (
	exists (
		select 1
		from public.robots r
		where r.id = robot_connections.robot_id
		  and r.owner_id = auth.uid()
	)
);

create policy "Robot connections can be updated by robot owner"
on public.robot_connections
for update
using (
	exists (
		select 1
		from public.robots r
		where r.id = robot_connections.robot_id
		  and r.owner_id = auth.uid()
	)
)
with check (
	exists (
		select 1
		from public.robots r
		where r.id = robot_connections.robot_id
		  and r.owner_id = auth.uid()
	)
);

-- robot sync events policies
create policy "Robot sync events are viewable by robot owner"
on public.robot_sync_events
for select
using (
	exists (
		select 1
		from public.robots r
		where r.id = robot_sync_events.robot_id
		  and r.owner_id = auth.uid()
	)
);

create policy "Robot sync events can be inserted by robot owner"
on public.robot_sync_events
for insert
with check (
	exists (
		select 1
		from public.robots r
		where r.id = robot_sync_events.robot_id
		  and r.owner_id = auth.uid()
	)
);

-- =============================
-- Helpful views
-- =============================
create or replace view public.user_robot_summary as
select
	p.id as user_id,
	p.display_name,
	r.id as robot_id,
	r.name as robot_name,
	r.connection_mode,
	r.remote_mode_enabled,
	r.last_seen_at,
	r.created_at
from public.profiles p
left join public.robots r on r.owner_id = p.id;

-- Grant access to authenticated users for their relevant rows.
-- This is intentionally kept narrow and should remain aligned with the RLS rules above.
grant usage on schema public to anon, authenticated;
grant select on public.user_robot_summary to authenticated;
