create schema if not exists private;
create table if not exists private.robot_claim_tokens (
  robot_id uuid primary key references public.robots(id) on delete cascade,
  token_hash text not null unique, expires_at timestamptz not null,
  claimed_by uuid references auth.users(id), claimed_at timestamptz,
  created_at timestamptz not null default now()
);
alter table private.robot_claim_tokens enable row level security;
revoke all on schema private from public, anon, authenticated;
revoke all on all tables in schema private from public, anon, authenticated;
grant usage on schema private to service_role;
grant all on all tables in schema private to service_role;

create table if not exists public.robot_mqtt_credentials (
  robot_id uuid primary key references public.robots(id) on delete cascade,
  mqtt_username text not null unique, password_ciphertext text not null,
  password_nonce text not null, key_version integer not null default 1,
  provisioned_at timestamptz not null default now(), rotated_at timestamptz not null default now()
);
alter table public.robot_mqtt_credentials enable row level security;
revoke all on public.robot_mqtt_credentials from public, anon, authenticated;
grant all on public.robot_mqtt_credentials to service_role;
alter table public.robots add column if not exists claimed_at timestamptz;
alter table public.robots add column if not exists mqtt_provisioned_at timestamptz;
drop policy if exists "Robots can be inserted by owner" on public.robots;
revoke insert on public.robots from authenticated;

create or replace function public._claim_arigator_robot(
  p_robot_code text, p_claim_token_hash text, p_user_id uuid
) returns table(robot_id uuid, robot_name text)
language plpgsql security definer set search_path = ''
as $$
declare v_robot_id uuid; v_robot_name text;
begin
  if p_user_id is null or p_robot_code is null or p_claim_token_hash is null then raise exception 'invalid_claim'; end if;
  select r.id, r.name into v_robot_id, v_robot_name
  from public.robots r join private.robot_claim_tokens c on c.robot_id = r.id
  where r.robot_code = p_robot_code and c.token_hash = p_claim_token_hash
    and c.claimed_at is null and c.expires_at > now() and r.owner_id is null
  for update of r, c;
  if v_robot_id is null then raise exception 'claim_invalid_or_already_used'; end if;
  update public.robots set owner_id = p_user_id, claimed_at = now(), updated_at = now()
  where id = v_robot_id and owner_id is null;
  if not found then raise exception 'claim_invalid_or_already_used'; end if;
  update private.robot_claim_tokens set claimed_by = p_user_id, claimed_at = now()
  where robot_id = v_robot_id and claimed_at is null;
  return query select v_robot_id, v_robot_name;
end;
$$;
revoke all on function public._claim_arigator_robot(text,text,uuid) from public, anon, authenticated;
grant execute on function public._claim_arigator_robot(text,text,uuid) to service_role;
