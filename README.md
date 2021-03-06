# SuperSimpleAlerts.NET

Designed to be a simple tool, deployed to Amazon Lambda, that disseminates alert messages to an IT team.

## Features

### Configuration
* Configure with a JSON file stored in S3
* Central list of contacts
* Subscribe contacts to certain alert levels or alert codes
* Create groups to simplify repetitive configuration

### Alert handlers
* Support for SMS via a Twilio account
* Support for email via AWS SNS (SMTP)
* Support for Slack via the Incoming Webhook plugin, including colorisation based on alert level or code

### Also
* Deduplication of alerts

## Who is it for?

This tool is useful if you have reasons for not signing up for enterprise-grade software like Pager Duty, or other alternatives like Cabot.

SuperSimpleAlerts.NET does not monitor your infrastructure or generate alerts - it only does one thing well, which is disseminating alert messages. A common scenario is to create other Lambda functions that monitor your system, invoking SuperSimpleAlerts.NET via an SNS message when a problem is detected.

## Required infrastructure
The code assumes that you are already have an AWS account and are familiar with Lambda.

The Lambda function is invoked via an SNS Topic.

In order to support deduplication of alerts, a place to store state is required. Currently only Redis via ElasticCache is supported for this purpose.
You may want to reuse an existing ElasticCache instance, as the cost of running an instance purely for alerts is likely to be overkill given only a small amount of data is stored.
In order to have Lambda access ElasticCache it is best to have a Virtual Private Cloud setup, otherwise ElasticCache must be made publicly accessible, posing a security concern.

Configuration is stored in an S3 bucket.

## Upcoming features

This is the very first release for the public. Over the coming weeks I will be adding the following features in anticipation of a release candidate:

* Vary subscriptions by time of day, day of week etc.
* Support for AWS SSM as an alternative to environment variables and S3 configuration
* Proper documentation
* Support for other alert handler endpoints such as HipChat
* Support for memcached as an alternative to Redis