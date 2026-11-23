"""SIM cost anomaly pipeline: reads billing history from the API, trains, evaluates and scores.

The pipeline never connects to PostgreSQL. The .NET API stays the only component with database
access; this package talks to its authenticated /api/integrations/ml endpoints.
"""
