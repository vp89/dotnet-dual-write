CREATE DATABASE orders;

\c orders

DROP TABLE IF EXISTS public.orders;

CREATE TABLE public.orders (
    id BIGINT GENERATED ALWAYS AS IDENTITY,
    amount DECIMAL(11,2) NOT NULL
);

CREATE TABLE public.orders_v2 (
    id BIGINT GENERATED ALWAYS AS IDENTITY,
    amount DECIMAL(11,2) NOT NULL
);


ALTER TABLE IF EXISTS public.orders_v2
    OWNER to postgres;

CREATE PUBLICATION test_pub;
