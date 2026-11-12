alter table public.robots
  add column if not exists hardware_id text;

create unique index if not exists robots_hardware_id_unique
  on public.robots (hardware_id)
  where hardware_id is not null;

create or replace function public._register_arigator_robot(
  p_hardware_id text,
  p_user_id uuid,
  p_robot_name text default 'A-Rigator'
)
returns table(robot_id uuid, robot_name text, already_registered boolean)
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_hardware_id text;
  v_robot_id uuid;
  v_robot_name text;
  v_owner_id uuid;
  v_already_registered boolean;
begin
  v_hardware_id := upper(regexp_replace(coalesce(p_hardware_id, ''), '[^A-Fa-f0-9]', '', 'g'));

  if p_user_id is null
     or length(v_hardware_id) <> 12
     or v_hardware_id !~ '^[A-F0-9]{12}$' then
    raise exception 'invalid_registration';
  end if;

  insert into public.robots (
    owner_id, name, hardware_id, connection_mode,
    remote_mode_enabled, claimed_at
  )
  values (
    p_user_id,
    left(coalesce(nullif(btrim(p_robot_name), ''), 'A-Rigator'), 48),
    v_hardware_id,
    'remote',
    false,
    now()
  )
  on conflict (hardware_id) where hardware_id is not null do nothing;

  select r.id, r.name, r.owner_id
    into v_robot_id, v_robot_name, v_owner_id
  from public.robots r
  where r.hardware_id = v_hardware_id
  for update;

  if v_robot_id is null then
    raise exception 'registration_failed';
  end if;

  if v_owner_id <> p_user_id then
    raise exception 'robot_already_registered';
  end if;

  v_already_registered := exists (
    select 1 from public.robots r
    where r.id = v_robot_id
      and r.created_at < now() - interval '2 seconds'
  );

  return query select v_robot_id, v_robot_name, v_already_registered;
end;
$$;

revoke all on function public._register_arigator_robot(text, uuid, text)
  from public, anon, authenticated;
grant execute on function public._register_arigator_robot(text, uuid, text)
  to service_role;

comment on column public.robots.hardware_id is
  'Stable ESP32 eFuse MAC identifier used for idempotent robot registration. Not a secret or proof of physical possession.';
